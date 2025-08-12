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
    public const string PrefixDelim = "🐬";
    public static readonly int PrefixDelimWidth = PrefixDelim.AsSpan().Length;

    /// <summary>When this is the first argument, use the second argument to grab the npc/summary>
    public const string NPCRef = "🎣";
    public static readonly int NPCRefWidth = NPCRef.AsSpan().Length;

    /// <summary>When this is the second argument, use the trimmed string from the trim delim as display name./summary>
    public const string NameFromTrim = "@";

    /// <summary>Default NPC display name</summary>
    private const string QQQ = "???";

    /// <summary>SimpleNonVillagerDialogues delim</summary>
    private const string SNVDDelim = "||";
    private const string SNVDAssetName = "Strings\\SimpleNonVillagerDialogues";
    private static readonly int SNVDDelimWidth = SNVDDelim.AsSpan().Length;

    /// <summary>Static monitor for logging in harmony postfix</summary>
    private static IMonitor? mon;

    /// <summary>Fake speaker NPC</summary>
    private static readonly Lazy<NPC> SpeakerNPC =
        new(() => new(null, Vector2.Zero, "", 0, QQQ, null, eventActor: false) { displayName = QQQ });

    /// <summary>String builder for processed dialogue</summary>
    private static readonly Lazy<StringBuilder> SB = new(() => new StringBuilder());

    /// <summary>Cached NPC.portrait field</summary>
    private static readonly FieldInfo portraitField = typeof(NPC).GetField(
        "portrait",
        BindingFlags.NonPublic | BindingFlags.Instance
    )!;

    /// <summary>Cached Dialogue.isLastDialogueInteractive field</summary>
    private static readonly FieldInfo isLastDialogueInteractiveField = typeof(Dialogue).GetField(
        "isLastDialogueInteractive",
        BindingFlags.NonPublic | BindingFlags.Instance
    )!;

    /// <summary>Cached Dialogue.playerResponses field</summary>
    private static readonly FieldInfo playerResponsesField = typeof(Dialogue).GetField(
        "playerResponses",
        BindingFlags.NonPublic | BindingFlags.Instance
    )!;

    /// <summary>Cached portrait field</summary>
    private static readonly MethodInfo setUpQuestionsMethod = typeof(DialogueBox).GetMethod(
        "setUpQuestions",
        BindingFlags.NonPublic | BindingFlags.Instance
    )!;

    /// <summary>Guard against ??? recursive scenario when we call ctor in the ctor postfix (unlikely)</summary>
    private static bool InPostfix = false;

    /// <summary>Hold on to the dialogue boxes (original, new) briefly to swap on menu changed</summary>
    private static (DialogueBox, DialogueBox)? SwapDialogueBox_Instant = new();

    public override void Entry(IModHelper helper)
    {
        if (portraitField == null)
        {
            throw new FieldAccessException("Failed to reflect into NPC.portrait, please report this error");
        }

        mon = Monitor;

        helper.Events.Display.MenuChanged += OnMenuChanged;
        helper.Events.Content.AssetRequested += OnAssetRequested;

        Harmony harmony = new(ModId);

        HarmonyMethod finalizer =
            new(typeof(ModEntry), nameof(DialogueBox_strings_ctor_Finalizer)) { priority = Priority.Last };
        harmony.Patch(
            original: AccessTools.DeclaredConstructor(typeof(DialogueBox), [typeof(string)]),
            finalizer: finalizer
        );
        harmony.Patch(
            original: AccessTools.DeclaredConstructor(typeof(DialogueBox), [typeof(List<string>)]),
            finalizer: finalizer
        );

        if (isLastDialogueInteractiveField != null || playerResponsesField != null)
        {
            harmony.Patch(
                original: AccessTools.DeclaredConstructor(
                    typeof(DialogueBox),
                    [typeof(string), typeof(Response[]), typeof(int)]
                ),
                finalizer: new(typeof(ModEntry), nameof(DialogueBox_question_ctor_Finalizer))
                {
                    priority = Priority.Last,
                }
            );
        }
        else
        {
            Log(
                "Failed to reflect into Dialouge.isLastDialogueInteractive and/or Dialogue.playerResponses, please report this error",
                LogLevel.Error
            );
        }
    }

    private void OnAssetRequested(object? sender, AssetRequestedEventArgs e)
    {
        if (e.NameWithoutLocale.IsEquivalentTo(SNVDAssetName))
        {
            e.Edit(Edit_StringsSimpleNonVillagerDialogues, AssetEditPriority.Late + 200);
        }
    }

    /// <summary>
    /// Mass edit Strings\\SimpleNonVillagerDialogues
    /// "blep blop🐬dialogue1||dialogue2" -> "blep blop🐬dialogue1||blep blop🐬dialogue2"
    /// </summary>
    /// <param name="asset"></param>
    private void Edit_StringsSimpleNonVillagerDialogues(IAssetData asset)
    {
        IDictionary<string, string> data = asset.AsDictionary<string, string>().Data;
        int idx;
        ReadOnlySpan<char> span;
        ReadOnlySpan<char> prefix;
        StringBuilder sb = SB.Value;
        sb.Clear();
        foreach ((string key, string value) in data)
        {
            span = value.AsSpan();
            if ((idx = span.IndexOf(PrefixDelim)) <= 1)
            {
                continue;
            }

            idx += PrefixDelimWidth;
            prefix = span[..idx];
            sb.Append(prefix);
            span = span[idx..];
            while ((idx = span.IndexOf(SNVDDelim)) > 0)
            {
                idx += SNVDDelimWidth;
                sb.Append(span[..idx]);
                sb.Append(prefix);
                span = span[idx..];
            }
            if (span.Length > 0)
            {
                sb.Append(span);
            }
            data[key] = sb.ToString();
        }
    }

    /// <summary>Swap in the new portrait DialogueBox</summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void OnMenuChanged(object? sender, MenuChangedEventArgs e)
    {
        // instant swap to the static menu
        if (SwapDialogueBox_Instant.HasValue && e.NewMenu == SwapDialogueBox_Instant.Value.Item1)
        {
            Game1.activeClickableMenu = SwapDialogueBox_Instant.Value.Item2;
            SwapDialogueBox_Instant = null;
        }
    }

    /// <summary>Try and make the portrait dialogue</summary>
    /// <param name="curr"></param>
    /// <param name="dialogues"></param>
    /// <returns></returns>
    private static Dialogue? TryMakePortraitDialogue(string curr, List<string> dialogues)
    {
        int idx;
        ReadOnlySpan<char> span;
        StringBuilder sb = SB.Value;
        sb.Clear();

        span = curr.Replace(Environment.NewLine, "").AsSpan();
        if ((idx = span.IndexOf(PrefixDelim)) <= 1)
        {
            return null;
        }
        string[] args = ArgUtility.SplitBySpaceQuoteAware(span[..idx].ToString());
        if (
            !ArgUtility.TryGet(
                args,
                0,
                out string speakerRef,
                out string error,
                allowBlank: false,
                name: "string speakerRef"
            )
            || !ArgUtility.TryGet(
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
            return null;
        }

        Texture2D? portrait;
        string? displayName = null;
        if (speakerRef == NPCRef)
        {
            NPC? realNPC = Game1.getCharacterFromName<NPC>(speakerName);
            if (realNPC == null)
            {
                Log($"Failed to find NPC '{speakerName}'", LogLevel.Warn);
                return null;
            }
            portrait = realNPC.Portrait;
            displayName = realNPC.getName();
        }
        else if (Game1.content.DoesAssetExist<Texture2D>(speakerRef))
        {
            portrait = Game1.content.Load<Texture2D>(speakerRef);
            if (speakerName != NameFromTrim)
                displayName = TokenParser.ParseText(speakerName);
        }
        else
        {
            Log($"Portrait '{speakerRef}' is invalid", LogLevel.Warn);
            return null;
        }

        int trimIdx;
        foreach (string dlog in dialogues)
        {
            span = dlog.Replace(Environment.NewLine, "").AsSpan();
            if ((trimIdx = span.IndexOf(PrefixDelim)) > -1)
                span = span[(trimIdx + PrefixDelimWidth)..];
            if (trimDelim != null && (trimIdx = span.IndexOf(trimDelim)) > -1)
            {
                displayName ??= span[..trimIdx].ToString();
                span = span[(trimIdx + trimDelim.AsSpan().Length)..];
            }
            sb.Append(span.Trim());
            sb.Append("#$b#");
        }
        sb.Remove(sb.Length - 4, 4);
        string final = sb.ToString();
        sb.Clear();

        NPC speaker = SpeakerNPC.Value;
        speaker.Name = QQQ;
        speaker.CurrentDialogue.Clear();
        portraitField.SetValue(speaker, portrait);
        speaker.displayName = displayName;

#if DEBUG
        Log($"Convert: '{string.Join(',', dialogues)}' -> '{final}'");
#endif

        return new(speaker, final, final);
    }

    /// <summary>Inspect and possibly create a new replacement DialogueBox</summary>
    /// <param name="__instance"></param>
    private static void DialogueBox_strings_ctor_Finalizer(DialogueBox __instance)
    {
        if (InPostfix)
            return;

        foreach (string dialogue in __instance.dialogues)
        {
            if (TryMakePortraitDialogue(dialogue, __instance.dialogues) is Dialogue charaDialogue)
            {
                InPostfix = true;
                DialogueBox charaDialogueBox = new(charaDialogue);
                SwapDialogueBox_Instant = new(__instance, charaDialogueBox);
                InPostfix = false;
                return;
            }
        }
    }

    /// <summary>Inspect and possibly create a new replacement DialogueBox for question dialogues</summary>
    /// <param name="__instance"></param>
    private static void DialogueBox_question_ctor_Finalizer(DialogueBox __instance)
    {
        if (InPostfix || __instance.dialogues.Count < 1)
            return;

        if (TryMakePortraitDialogue(__instance.dialogues[0], __instance.dialogues) is Dialogue charaDialogue)
        {
            InPostfix = true;
            DialogueBox charaDialogueBox = new(charaDialogue);

            // Extremely cursed way of forcing a location question dialogue in characterDialogue mode
            // First, set all the stuff for a character question dialogue (i.e. $q)
            // Then, use DialogueLine.SideEffects and go back to location question dialogue 1 tick after the dialogue is processed
            charaDialogue.dialogues.Add(
                new DialogueLine(
                    "{",
                    () => DelayedAction.functionAfterDelay(() => charaDialogueBox.characterDialogue = null, 0)
                )
            );
            charaDialogue.dialogues.Add(new DialogueLine(" "));
            charaDialogue.isCurrentStringContinuedOnNextScreen = true;
            isLastDialogueInteractiveField.SetValue(charaDialogue, true);
            playerResponsesField.SetValue(
                charaDialogue,
                __instance
                    .responses.Select(resp => new NPCDialogueResponse(
                        resp.responseKey,
                        0,
                        resp.responseKey,
                        resp.responseText
                    ))
                    .ToList()
            );

            SwapDialogueBox_Instant = new(__instance, charaDialogueBox);
            InPostfix = false;
            return;
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
