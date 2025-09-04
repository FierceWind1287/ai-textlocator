# TextLocator AI Assistant

**TextLocator AI Assistant** is a local AI desktop solution designed for Intel laptops and ultrabooks.  
It is optimized for **offline** and **privacy-safe** use cases.  

The project integrates **local semantic search** into the open-source desktop search application [TextLocator](https://github.com/liulei901112/TextLocator) (WPF / .NET Framework 4.6.1).  
The workflow is: **Natural Language / Voice → Keywords → Classic Full-Text Search**.  
Runs on **CPU**, with optional **GPU/NPU acceleration**.

---

## Repository Contents

This repository contains the final runnable version and key source code:

- **Parent Application**:  
  `TextLocator/` – WPF UI with **Lucene.Net + Jieba.NET** indexing and search.

- **Sidecar Service**:  
  `KeywordService.exe` (.NET 8 + LlamaSharp)  
  Loads **Granite 3.3 2B Instruct** model locally.  
  Outputs: *“a single line, comma-separated, lowercase, 3–5 keywords”*.

- **Local Speech Bridge**:  
  `WhisperBridge.dll` (OpenVINO + Distil-Whisper native bridge).  
  Invoked from WPF side via `WhisperNative.cs`.

- **Models**:  
  Place models in the `Models/` folder. Examples:  
  - `Models/granite-3.3-2b-instruct-Q4_K_M.gguf`  
  - `Models/Whisper` (IR / weights)

---

## Releases

The **Releases** section provides:

- **Distributable installer / archive**  
  Extract or install, then run `TextLocator.exe`.  
  On the **AI Page**, enter natural language text or click the microphone.  
  After progress completes, you will be redirected to the classic search page with results.

- **Source Code Packages**  
  - `TextLocator/` → WPF main project  
  - `KeywordService/` → Sidecar service  
  - `WhisperBridge/` → Native bridge  

  When building manually:  
  - Ensure `KeywordService.exe` is in the same directory as `TextLocator.exe`.  
  - Prepare the required models under `Models/`.

---

## License & Acknowledgments

This project follows and complies with the licenses of:  
- **TextLocator**  
- **LlamaSharp**  
- **OpenVINO / Whisper**  

All components in the program (including ai models, and open source executable program) are with the freely-distributable and alterble license: MIT, Apache 2.0 or GPL license.
The release package includes **third-party NOTICE / License** files and model license statements.
