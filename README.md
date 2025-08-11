# PortraitsForAll

Tiny framework mod that bonks various no portrait DialogueBox to DialogueBox.
Made for use in *Portraits for Extras*.

## Usage

This is primarily for usage with content patcher text operation Prepend on string assets.
See [example pack here]([CP]%20Planted/content.json) for details.

The actual full syntax for putting portraits in arbitrary dialogue is:
<Portrait OR 🎣> <SpeakerName OR NPCInternalName> [TrimDelim]🐬rest of the dialogue etc...
- Portrait: portrait asset OR 🎣
- SpeakerName (Arg1!=🎣): speaker name
     can use tokenized text like [CharacterName Krobus]
     can use @ to take content before trim delim
     defaults to ??? if not given
- NPCInternalName (Arg1==🎣): npc internal name
     fetches the actual NPC instance and try to get portrait + name from them
- TrimDelim: optional first delimiter to trim to,
             everything before first instance of this delimiter will be discarded

If the dialogue is a list, only the first string with 🐬 will be considered for metadata, i.e. you cannot change the portrait asset in-between dialogues.
Normal dialogue syntax work, but not `$e`.


