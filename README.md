# PortraitsForAll

Tiny framework mod that bonks various no portrait DialogueBox to DialogueBox.
Made for use in *Portraits for Extras*.

## Usage

This is primarily for usage with content patcher text operation Prepend on string assets.
See [example pack here]([CP]%20Planted/content.json) for details.

The actual full syntax for arbitrary dialogue is:
<Portrait> [SpeakerName] [trimDelim]🐬rest of the dialogue etc...
- Portrait: portrait asset
- DisplayName: speaker name, can use tokenized text like [LocalizedText Strings\\NPCNames:Krobus], defaults to ??? if not given
- TrimDelim: optional first delimiter to trim to,
             everything before first instance of this delimiter will be discarded

If the dialogue is a list, only the first string with 🐬 will be considered for metadata, i.e. you cannot change the portrait asset in-between dialogues.
Normal dialogue syntax work, but not `$e`.
