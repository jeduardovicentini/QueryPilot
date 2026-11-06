# Challenges recorrentes: SqlTools (extensão SSMS 22)

- **Testar sem UI.** A máquina pode estar com a tela bloqueada. Use `tools/Run-SelfTest.ps1`, que roda o SelfTest.cs dentro do SSMS, e `RenderTargetBitmap` para as imagens. Screenshot comum sai preto.
- **Diálogos modais travam a automação.** O COM devolve `RPC_E_CALL_REJECTED`. Para descobrir qual é o diálogo, use `tools/SsmsUi.ps1` (EnumWindows) ou UIA no handle.
- **Linha de comando do SSMS 22:** `-S <srv> -C -nosplash`. Não existe `-E` nem `/log`.
- **Reinstalar a mesma versão:** o VSIXInstaller recusa; desinstale antes (o build.ps1 -Install já faz isso).
- **Menu invisível:** o SSMS esconde menus de topo de extensões que não estão em `Menus\AllowedExtensionMenus`, e o self-test via DTE não pega isso. Confira a barra de menus por UI Automation depois de instalar.
- **Internals do SSMS:** confirme os nomes de campos e eventos inspecionando as DLLs (ver o padrão em README → pontos de integração) antes de assumir. O sender dos eventos não é necessariamente o objeto dono.
