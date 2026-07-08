# Chat Error Notes

## Korean IME submit drops the last character

### Fixed status
- Fixed on 2026-07-08.
- Main file: `Assets/Player/Script/UI/BattleChatUI.cs`
- Network file checked: `Assets/Player/Script/UI/BattleChatNetwork.cs`

### User-visible symptom
- Open chat with Enter, type Korean text, then press Enter to submit.
- The submitted chat message could miss the final composing character.
- The input field could also keep the final composing character after submit in earlier versions.

### Repro cases
- `ㅎㅇㅎㅇ` was sent as `ㅎㅇㅎ`.
- `아아` was sent as `아`.
- `아아아` was sent as `아아`.
- `안녕` and `아이` could work normally, which made the bug look inconsistent.

### Expected behavior
- After `Enter -> type chat -> Enter`, the input field must be empty.
- The chat log must contain every character the player typed.
- Repeated same Korean characters and Korean consonant/vowel-only text must be sent intact.

### Root cause
- Korean IME composition can keep the last character in `Input.compositionString` before it is committed into `TMP_InputField.text`.
- Reading only `TMP_InputField.text` at submit time can therefore miss the final composing character.
- Deactivating the input field before taking a snapshot can also clear `Input.compositionString`, so the final composition must be captured immediately when Enter/onSubmit is received.
- A previous duplicate-prevention guard used `text.EndsWith(composition)`. This was wrong for repeated same characters: in `아아`, the field text was `아` and the active composition was also `아`, so the guard incorrectly dropped the second `아`.

### Final fix
- `BuildCurrentInputText()` reads `_input.text` and always appends non-empty `Input.compositionString`.
- `QueueSubmitCurrentText()` captures this pre-deactivation text immediately when Enter is pressed.
- `QueueSubmittedText()` also compares the TMP submitted text with the current text-plus-composition snapshot and keeps the longer/more complete text.
- `CoSubmitAfterInputSettles()` then deactivates the input field, waits for TMP/IME to settle, reads the settled field text, and sends the more complete value between the pre-submit snapshot and settled text.
- After sending, `SetTyping(false)` turns IME composition off, makes the field non-interactable, and clears the field.
- `CoClearInputAfterImeSettles()` clears the field for several frames so late IME commits cannot leave stray text visible.

### Important implementation details
- Do not restore `text.EndsWith(composition)` or any equivalent duplicate-prevention check without a Korean repeated-character test.
- Do not move the first `BuildCurrentInputText()` call to after `_input.DeactivateInputField()`, because deactivation can clear the composition string.
- Keep `_isSubmitting` so Enter and TMP `onSubmit` do not double-send the same message.

### Verification checklist
- `ㅎㅇㅎㅇ` sends `ㅎㅇㅎㅇ`.
- `아아` sends `아아`.
- `아아아` sends `아아아`.
- `안녕` sends `안녕`.
- `아이` sends `아이`.
- `안녕하세요` sends `안녕하세요`.
- After submit, the input field is empty.
- Build check used: `dotnet build Battle_PVP.sln -m:1 -nodeReuse:false -v:q`.
