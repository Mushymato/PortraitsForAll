using System.Reflection;
using System.Text;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Menus;
using StardewValley.TokenizableStrings;

namespace PortraitsForAll;

public sealed class ModEntry : Mod
{
    /// <summary>Log level</summary>
#if DEBUG
    private const LogLevel DEFAULT_LOG_LEVEL = LogLevel.Debug;
#else
    private const LogLevel DEFAULT_LOG_LEVEL = LogLevel.Trace;
#endif

    /// <summary>Mod Id</summary>
    public const string ModId = "mushymato.PortraitsForAll";

    /// <summary>Delimiter for PortraitsForAll metadata/summary>
    public const string Delim = "🐬";
    public static readonly int DelimWidth = Delim.AsSpan().Length;

    /// <summary>Default NPC display name</summary>
    private const string QQQ = "???";

    /// <summary>Static monitor for logging in harmony postfix</summary>
    private static IMonitor? mon;

    /// <summary>Fake speaker NPC</summary>
    private static readonly Lazy<NPC> SpeakerNPC =
        new(() => new(null, Vector2.Zero, "", 0, QQQ, null, eventActor: false) { displayName = QQQ });

    /// <summary>String builder for processed dialogue</summary>
    private static readonly Lazy<StringBuilder> SB = new(() => new StringBuilder());

    /// <summary>Cached portrait field</summary>
    private static readonly FieldInfo portraitField = typeof(NPC).GetField(
        "portrait",
        BindingFlags.NonPublic | BindingFlags.Instance
    )!;

    /// <summary>Guard against ??? recursive scenario when we call ctor in the ctor postfix (unlikely)</summary>
    private static bool InPostfix = false;

    /// <summary>Hold on to the dialogue boxes (original, new) briefly to swap on menu changed</summary>
    private static (DialogueBox, DialogueBox)? SwapDialogueBox = new();

    public override void Entry(IModHelper helper)
    {
        mon = Monitor;

        helper.Events.Display.MenuChanged += OnMenuChanged;

        Harmony harmony = new(ModId);
        HarmonyMethod postfix = new(typeof(ModEntry), nameof(DialogueBox_ctor_Postfix)) { priority = Priority.Last };

        harmony.Patch(
            original: AccessTools.DeclaredConstructor(typeof(DialogueBox), [typeof(string)]),
            postfix: postfix
        );
        harmony.Patch(
            original: AccessTools.DeclaredConstructor(typeof(DialogueBox), [typeof(List<string>)]),
            postfix: postfix
        );
    }

    /// <summary>Swap in the new portrait DialogueBox</summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void OnMenuChanged(object? sender, MenuChangedEventArgs e)
    {
        if (!SwapDialogueBox.HasValue)
            return;
        if (e.NewMenu == SwapDialogueBox.Value.Item1)
        {
            Game1.activeClickableMenu = SwapDialogueBox.Value.Item2;
            SwapDialogueBox = null;
        }
    }

    /// <summary>Inspect and possibly create a new replacement DialogueBox</summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private static void DialogueBox_ctor_Postfix(DialogueBox __instance)
    {
        if (InPostfix || __instance.isPortraitBox())
            return;

        int idx;
        ReadOnlySpan<char> span;
        foreach (string dialogue in __instance.dialogues)
        {
            span = dialogue.Replace(Environment.NewLine, "").AsSpan();
            if ((idx = span.IndexOf(Delim)) <= 1)
            {
                continue;
            }
            string[] args = ArgUtility.SplitBySpaceQuoteAware(span[..idx].ToString());
            if (
                !ArgUtility.TryGet(
                    args,
                    0,
                    out string portraitAsset,
                    out string error,
                    allowBlank: false,
                    name: "string portraitAsset"
                )
                || !ArgUtility.TryGetOptional(
                    args,
                    1,
                    out string? speakerName,
                    out error,
                    allowBlank: false,
                    name: "string speakerName"
                )
                || !ArgUtility.TryGetOptional(
                    args,
                    2,
                    out string? trimDelim,
                    out error,
                    allowBlank: false,
                    name: "string trimDelim"
                )
            )
            {
                Log(error, LogLevel.Warn);
                continue;
            }
            if (!Game1.content.DoesAssetExist<Texture2D>(portraitAsset))
            {
                Log($"Portrait '{portraitAsset}' is invalid", LogLevel.Warn);
                continue;
            }

            NPC speaker = SpeakerNPC.Value;
            speaker.Name = QQQ;
            speaker.displayName = TokenParser.ParseText(speakerName) ?? QQQ;
            speaker.CurrentDialogue.Clear();
            portraitField.SetValue(speaker, Game1.content.Load<Texture2D>(portraitAsset));

            StringBuilder sb = SB.Value;
            sb.Clear();
            int trimIdx;
            foreach (string dlog in __instance.dialogues)
            {
                span = dlog.Replace(Environment.NewLine, "").AsSpan();
                if ((trimIdx = span.IndexOf(Delim)) > -1)
                    span = span[(trimIdx + DelimWidth)..];
                if (trimDelim != null && (trimIdx = span.IndexOf(trimDelim)) > -1)
                    span = span[(trimIdx + trimDelim.AsSpan().Length)..];
                sb.Append(span.Trim());
                sb.Append("#$b#");
            }
            sb.Remove(sb.Length - 4, 4);
            string final = sb.ToString();
#if DEBUG
            Log($"Convert: '{string.Join(',', __instance.dialogues)}' -> '{final}'");
#endif

            InPostfix = true;
            Dialogue charaDialogue = new(speaker, final, final);
            DialogueBox charaDialogueBox = new(charaDialogue);
            SwapDialogueBox = new(__instance, charaDialogueBox);
            InPostfix = false;

            break;
        }
    }

    /// <summary>SMAPI static monitor Log wrapper</summary>
    /// <param name="msg"></param>
    /// <param name="level"></param>
    internal static void Log(string msg, LogLevel level = DEFAULT_LOG_LEVEL)
    {
        mon!.Log(msg, level);
    }
}
