# Plano: v1 (histórico de queries + cor por servidor)

Escopo aprovado: ver conversa de 2026-11-06 e `analises/2026-11-06-paridade-ssms/v1/analise.md`.
Detalhes técnicos e pontos de integração: `README.md`.

## Checklist v0.1
- [x] Estrutura: Product.props (nome centralizado), VSIX SDK-style, manifest/vsct por modelo, build.ps1
- [x] Package carrega no SSMS 22 (instalação por usuário via VSIXInstaller do SSMS, sem admin)
- [x] HistoryStore (SQLite) + testes unitários (26)
- [x] Captura de execução: texto/seleção, servidor, banco, login, duração, linhas, status
- [x] Janela de histórico (layout C): filtros, busca, preview, abrir em nova aba conectada, copiar
- [x] Cor da aba por servidor (Adorner) + regras em Opções
- [x] Atalho Ctrl+Q, Ctrl+E (assumido uma vez, mesmo com o ApexSQL instalado)
- [x] Self-test automatizado dentro do SSMS (LocalDB): 18 checks
- [x] README, Instalar.cmd e Desinstalar.cmd

## Lições (SSMS 22)
- A linha de comando do SSMS 22 não aceita `-E` nem `/log`. Com `-S`, ele pede confirmação ("Connect to the following server?").
- O sender de `ScriptExecutionCompleted` é o `QEOLESQLExec`, não o controle de resultados.
- O template das abas muda por estado (ativa/inativa): pintar `Border` local não funciona, o `Adorner` sim.
- Shell VS 18: um atalho do .vsct já usado por outra extensão não é aplicado; precisa de `Command.Bindings`.
- Com a tela bloqueada, `CopyFromScreen` sai preto; `RenderTargetBitmap` da janela WPF funciona.
- O SSMS esconde menus de topo de extensões (`Menus\UnexpectedTopLevelMenus`) e não os mostra em Extensions. Só aparecem os que estão em `Menus\AllowedExtensionMenus` do privateregistry.bin. Solução: `tools/Allow-TopMenu.ps1` no instalador + `EnsureTopMenuAllowed` no package + submenu em Tools.
- Para verificar menus, use UI Automation (MenuBar) e não o `IsAvailable` do DTE: o comando existir não significa que está visível.

## Próximo (v0.2): autocomplete
- Cache de metadados por conexão (reaproveitar `SsmsBridge.GetEditorConnection`)
- Parser: ScriptDom do próprio SSMS
- Depois: histórico de abas fechadas / restaurar sessão, alerta de DELETE/UPDATE sem WHERE
