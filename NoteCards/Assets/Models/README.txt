If the selected model file is missing from this folder, the app will auto-download the required `.gguf` model on first AI conversion.
Downloaded models are stored in `%LocalAppData%\\NoteCards\\AiCache\\models`.
Optional debug override URL: environment variable `NOTECARDS_QWEN_MODEL_URL` (Debug builds only).
Integrity checksum variable: `NOTECARDS_QWEN_MODEL_SHA256` (SHA-256, hex).
In Release builds, downloaded models require a checksum value.
