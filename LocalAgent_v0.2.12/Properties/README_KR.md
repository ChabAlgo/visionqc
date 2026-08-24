# Properties

Assembly 메타데이터만 관리한다. Agent와 Installer의 `AssemblyVersion`/`AssemblyFileVersion`은 릴리스 Agent 버전과 함께 올린다. Web 버전과 Agent 버전은 독립적이므로 `Program.AgentVersion`, `OfflineInstaller.Program.ProductVersion`, Web의 `EXPECTED_AGENT_VERSION`도 동시에 확인한다.
