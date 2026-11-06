# Challenges recorrentes: SqlTools (extensão SSMS 22)

- **Testar sem UI.** A máquina pode estar com a tela bloqueada. Use `tools/Run-SelfTest.ps1`, que roda o SelfTest.cs dentro do SSMS, e `RenderTargetBitmap` para as imagens. Screenshot comum sai preto.
- **Diálogos modais travam a automação.** O COM devolve `RPC_E_CALL_REJECTED`. Para descobrir qual é o diálogo, use `tools/SsmsUi.ps1` (EnumWindows) ou UIA no handle.
- **Linha de comando do SSMS 22:** `-S <srv> -C -nosplash`. Não existe `-E` nem `/log`.
- **Reinstalar a mesma versão:** o VSIXInstaller recusa; desinstale antes (o build.ps1 -Install já faz isso).
- **Menu invisível:** o SSMS esconde menus de topo de extensões que não estão em `Menus\AllowedExtensionMenus`, e o self-test via DTE não pega isso. Confira a barra de menus por UI Automation depois de instalar.
- **Internals do SSMS:** confirme os nomes de campos e eventos inspecionando as DLLs (ver o padrão em README → pontos de integração) antes de assumir. O sender dos eventos não é necessariamente o objeto dono.
- **Autocomplete:** a "sessão legada" ativa costuma ser o shim da nossa própria sessão async; antes de concluir que o IntelliSense nativo abriu, olhe o tipo da sessão.
- **Metadados lentos:** se a query de catálogo travar, olhe `sys.dm_exec_requests.wait_type`. RESOURCE_SEMAPHORE = memory grant; evite com hints e mantenha o catálogo de sistema separado.
- **Escapes no shell:** neste ambiente, `\r\n` dentro de heredoc do Bash chega ao Python como quebra de linha de verdade. Para editar literais C# com `\r\n`/`\n`, use a ferramenta Edit (ou Write), não scripts no shell.
- **Commit do autocomplete no Tab:** o SSMS insere o item pelo caminho padrão mesmo quando o commit manager devolve "tratado" (o texto saiu duplicado). Ajustes depois da inserção vão no `session.ItemCommitted`.
- **SSMS do usuário aberto:** nunca instalar com ele aberto. Compile sem `-Install`, espere em background até o processo `Ssms` sumir e só então instale e rode o self-test.
- **Seção no arquivo de snippets:** linhas `-- =====` encerram o snippet anterior (senão o título entra no texto).
