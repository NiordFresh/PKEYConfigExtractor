![pkey](https://github.com/user-attachments/assets/603fd9c9-db35-4930-b25f-3caa20d8f58f)
# PKEYConfigExtractor
PKEYConfigExtractor is a WPF-based utility designed to generate Windows product keys using embedded PKEY configuration data (.xrm-ms files). Unlike earlier approaches that relied on external Python scripts, this tool integrates a high-performance key generation engine rewritten entirely in Go (Golang). By leveraging official PKEYConfig structures and implementing core cryptographic logic in Go for improved speed and reliability, it enables users to produce valid-looking product keys for various Windows editions—without external dependencies or runtime overhead.

---
# 🎯 Features

- User-Friendly GUI: A clean, responsive WPF interface that makes key generation straightforward—even for non-technical users. All controls are intuitively laid out, with clear visual feedback and real-time status updates.

- Zero External Dependencies: The application relies solely on the built-in WPF framework (.NET Framework 4.5.2+), with no third-party libraries or runtime requirements. This ensures maximum compatibility and portability across Windows systems.

- Blazing-Fast Key Generation: The core cryptographic engine is implemented in Go (Golang) and embedded directly into the application, enabling rapid key computation without the overhead of interpreted scripts or external processes.

- Self-Contained Execution: The Go-based generator is compiled into a native binary and bundled as an internal resource, eliminating the need for Python, .NET Core, or any other interpreters.

- Offline & Secure: No internet connection is required.

- Minimal System Footprint: Small executable size, low memory usage, and quick startup time—ideal for use on older or resource-constrained systems.


---
# ⚙️ Requirements:

```
- .NET Framework 4.5.2 or higher  
```

---
# 📜 Licence

```
This project is licensed under the GPL-3.0 License. See the LICENSE file for more information.
```

---
# ⚠️ Disclaimer

> This software, PKEYConfigExtractor, is provided strictly for educational, academic, and research purposes. The author explicitly DISCLAIMS any responsibility or liability for misuse, illegal activity, copyright infringement, software piracy, or violation of Microsoft’s Software License Terms.

