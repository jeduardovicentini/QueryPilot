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

## v0.2: autocomplete (entregue em 2026-11-06)

Spike (2026-11-06): o editor SQL tem content type `SQL` (base `code`). Uma `IAsyncCompletionSource` MEF é chamada e aparece na sessão. O IntelliSense nativo (RadLangSvc, legado) **não** participa da sessão moderna; ele abre a própria lista.

Decisões:
- Autocomplete completo do QueryPilot (async completion MEF). Quando ligado, desliga só o "Auto list members" do IntelliSense nativo para T-SQL (os sublinhados de erro e o Quick Info continuam); quando desligado, devolve o valor original.
- Tokenizer próprio em Core, e não o ScriptDom: precisa funcionar com SQL incompleto enquanto se digita, ser testável e não depender da versão do SSMS.
- Metadados: uma conexão própria por servidor+banco, criada pelo caminho do SSMS (`UIConnectionInfoUtil` → `SqlConnectionInfo.CreateConnectionObject`, que cobre todos os tipos de autenticação). Um batch com vários result sets (schemas, objetos, colunas, PKs, FKs, bancos). Cache em memória; comando "Atualizar metadados".

Checklist:
- [x] Spike: content type + MEF participando
- [x] Core: tokenizer T-SQL (tolerante a texto incompleto)
- [x] Core: análise de contexto (FROM/JOIN/., EXEC, USE, ON, INSERT, @, *, expressão) e tabelas no escopo com alias
- [x] Core: modelo de metadados + loader (IDataReader) + SQL do catálogo
- [x] Core: engine de sugestões: keywords/funções, objetos, colunas, alias automático, JOIN por FK, condições ON, expandir `*`, lista de colunas do INSERT, variáveis
- [x] Ssms: serviço de metadados (conexão via SSMS, cache, atualizar)
- [x] Ssms: completion source (gatilhos ao digitar, descrição com tipo/colunas)
- [x] Ssms: liga/desliga o "Auto list members" nativo + opção nas Opções
- [x] Self-test com banco de teste no LocalDB (FKs) + verificação de que só uma lista abre

Lições da v0.2:
- A lista nativa não passa pelo broker moderno; o `ICompletionBroker` legado só mostra um *shim* da nossa sessão (`ShimCompletionSession`).
- O RadLangSvc relê as preferências ao abrir o editor: reaplicar o "Auto list members" em `TextViewCreated`.
- Sessão de completion precisa fechar em espaço/pontuação (commit manager); senão o span cresce e o Tab troca a linha inteira.
- Metadados: nada de `sys.all_*` nem variável de tabela (10-30 s); sistema separado por servidor; `OPTION (LOOP JOIN)` para não pedir memory grant (RESOURCE_SEMAPHORE no LocalDB ocupado).

## v0.3: autocomplete completo (entregue em 2026-11-06)
- [x] Tabelas temporárias (`CREATE TABLE #t`, `SELECT ... INTO #t`) e variáveis de tabela (`DECLARE @t TABLE`) com colunas e tipos
- [x] Parâmetros da procedure no `EXEC` (todos de uma vez ou os pendentes)
- [x] Snippets embutidos (ssf, st, btc, bte, ij, lj...)
- [x] Self-test: parâmetros, temporária e snippet digitado + Tab (34 checks)

## v0.4: formatador (entregue em 2026-11-06)
- [x] Motor próprio sobre o SqlTokenizer (ScriptDom descarta comentários e não aceita SQL incompleto)
- [x] Verificação token a token antes de aplicar; string/comentário aberto → não formata
- [x] Comando `Ctrl+K, Ctrl+Y` (seleção ou documento, um undo, cursor preservado) e opções (caixa, recuo, vírgula)
- [x] Testes: 18 casos de layout + idempotência + todos os cortes de 9 scripts; self-test com 39 checks

Lições: caixa só em palavras reservadas (ou contexto certo: `FETCH NEXT`, `OUTPUT` de parâmetro) — colunas como `Offset`/`Output` não podem mudar, porque bancos com collation CS quebrariam.

## v0.5: proteção e abas fechadas (entregue em 2026-11-06)
- [x] Alerta de DELETE/UPDATE sem WHERE e TRUNCATE no BeforeExecute do Query.Execute (cancela de verdade), reaproveitando SegmentStatements
- [x] Abas fechadas no mesmo SQLite (Status = ClosedTab), sem duplicar texto igual; `Ctrl+Q, Ctrl+T` e seletor na janela do histórico
- [x] Self-test: "Não" cancela e "Sim" executa (conferido no banco); fechar e reabrir no mesmo banco (44 checks)

Lição: no DocumentClosing o frame já saiu da enumeração de janelas; a conexão é lembrada antes (OnActiveFrameChanged + depois de executar) e, se a janela que fecha ainda é a ativa, lida na hora.

## v0.6: F12 e dica no mouse (entregue em 2026-11-06)
- [x] SymbolResolver: objeto/coluna sob o cursor (nome, schema.nome, alias, alias.coluna, coluna sem qualificador)
- [x] F12 = comando próprio (o SSMS não tem Edit.GoToDefinition): tabela pelo catálogo, módulos por OBJECT_DEFINITION → ALTER
- [x] Quick info assíncrono (IAsyncQuickInfoSource) com colunas/parâmetros
- [x] Self-test: F12 em tabela, alias.coluna e procedure; tooltip visível (53 checks)

Lições: o SMO dentro do SSMS levou ~32 s por tabela (Script()); o CREATE TABLE montado do catálogo numa consulta sai na hora. O TriggerQuickInfoAsync pode devolver null antes das fontes terminarem; a sessão fica em broker.GetSession(view).

## v0.7: modo de teste (entregue em 2026-11-06)
- [x] TestMode.Wrap: `BEGIN TRAN; ` na mesma linha (ou em lote próprio antes de CREATE/ALTER) + lote final `IF @@TRANCOUNT > 0 ROLLBACK`
- [x] Por aba (propriedade do ITextBuffer), Ctrl+Q, Ctrl+R, faixa no editor (adornment)
- [x] BeforeExecute envolve e seleciona o texto; AfterExecute desfaz pelo undo do editor (Ctrl+Z não traz o wrapper de volta)
- [x] Recusa COMMIT/ROLLBACK no texto; aviso se a execução for cancelada no meio
- [x] Self-test: UPDATE/INSERT desfeitos, texto e seleção restaurados, CREATE PROCEDURE desfeito, COMMIT recusado (65 checks)

Lição: o SSMS lê o texto a executar dentro do próprio comando Query.Execute, então dá para alterar o buffer no BeforeExecute e restaurar no AfterExecute.

## Depois
- Autocomplete: cross-database (`banco.schema.`), snippets do usuário (arquivo), colunas de funções de tabela com parâmetros
- Histórico de abas fechadas / restaurar sessão, alerta de DELETE/UPDATE sem WHERE, schema compare (DacFx)
- Formatador: alinhar `=` / aliases em colunas, perfis de estilo
