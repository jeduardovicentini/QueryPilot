# QueryPilot

Extensão gratuita e de código aberto para o **SQL Server Management Studio 22**.

## Autocomplete (v0.2 e v0.3)

Substitui a lista automática do IntelliSense nativo (os sublinhados de erro e o Quick Info do SSMS continuam). A lista abre sozinha ao começar uma palavra, depois de `.`, `@` e de `FROM ␣`, `JOIN ␣`, `EXEC ␣`, `USE ␣`, `ON ␣`; `Ctrl+Espaço` abre em qualquer lugar.

- **Tabelas com alias automático:** `FROM Cust` + Tab → `FROM Customer c` (schema só quando não é o padrão: `sales.Product p`).
- **JOIN completo pelas FKs:** depois de `JOIN`, sugere `[Order] o ON o.CustomerID = c.CustomerID` para cada FK entre as tabelas já usadas e as outras.
- **Condição do ON:** `JOIN Customer c ON ␣` → `c.CustomerID = o.CustomerID`.
- **Colunas no escopo:** no SELECT, WHERE, ORDER BY etc., as colunas das tabelas do FROM (mesmo o FROM vindo depois do cursor). Nome repetido em duas tabelas aparece como `c.Id` / `o.Id`.
- **`alias.`** lista as colunas; **`schema.`** lista os objetos; **`sys.`** as views de sistema.
- **Expandir `*` com Tab:** cursor no `*` (antes ou depois) + `Tab` → `SELECT * FROM oCarrinho` vira `SELECT oc.Col1, oc.Col2, ... FROM oCarrinho oc`, já formatado (uma coluna por linha, com as opções do formatador; um `Ctrl+Z` desfaz). Se a tabela não tem alias, ele é criado (e `oCarrinho.col` na mesma instrução vira `oc.col`); `o.*` expande só aquela tabela. `Ctrl+Espaço` no `*` continua mostrando o item na lista.
- **INSERT completo com Tab:** `INSERT INTO oCarrinho` + `Tab` no fim gera o INSERT no layout do "Script as INSERT" do SSMS — colunas que aceitam valor (sem identity, calculadas e rowversion) e um `<Coluna>` para cada valor, com o primeiro já selecionado.
- **INSERT:** `INSERT INTO t ␣` sugere `(col1, col2, ...)` sem identity, calculadas e rowversion.
- **EXEC** sugere procedures (com parâmetros na descrição); **USE**, bancos; **@**, variáveis do batch.
- CTEs e subconsultas expõem as colunas do SELECT delas.
- **Tabelas temporárias e variáveis de tabela:** `CREATE TABLE #t (...)`, `SELECT ... INTO #t` e `DECLARE @t TABLE (...)` do script entram no FROM e expõem as colunas (com tipo).
- **Parâmetros no EXEC:** `EXEC proc ␣` sugere `(todos os parâmetros)` → `@id = NULL, @saida = @saida OUTPUT` e cada parâmetro que ainda falta; depois de cada vírgula, só os pendentes.
- **Snippets:** `ssf` + Tab → `SELECT * FROM`; também `st` (TOP 100), `scf` (COUNT), `ii`, `df`, `bt`/`ct`/`rt` (transação), `btc` (TRY/CATCH), `bte` (transação com TRY/CATCH), `cte`, `ifex`, `ctt`, `snc`; no meio da instrução: `ij`, `lj`, `rj`, `ca`, `oa`, `ob`, `gb`, `wnl`, `isn`, `inn`.
- **Confirmar:** Tab ou Enter. Espaço e pontuação só confirmam quando o digitado é exatamente o item, então alias e valores digitados nunca são trocados.

Os metadados vêm de uma conexão própria criada pelo caminho do SSMS (qualquer tipo de autenticação), ficam em memória e recarregam sozinhos depois de um CREATE/ALTER/DROP executado. Também dá para forçar em *QueryPilot › Atualizar metadados do autocomplete*. Para voltar ao IntelliSense nativo: *QueryPilot › Opções* e desmarque o autocomplete; a lista nativa volta como estava.

## F12 e dica no mouse (v0.6)

- **F12** no nome de uma tabela, view, procedure ou função — ou num alias/coluna dela — abre a definição numa aba nova, conectada ao mesmo servidor e banco:
  - **tabela:** `CREATE TABLE` com colunas (tipo, identity, calculadas, defaults), PK/UNIQUE, CHECK, FKs (com ON DELETE/UPDATE) e índices (INCLUDE, filtro);
  - **procedure/view/função/gatilho:** o código como `ALTER`, igual ao "Modify" do SSMS (com `SET ANSI_NULLS`/`QUOTED_IDENTIFIER` do objeto).
- **Mouse parado** sobre uma tabela mostra as colunas com tipo, PK e identity; sobre uma procedure, os parâmetros; sobre `alias.coluna`, o tipo da coluna.

## Proteção e abas fechadas (v0.5)

- **DELETE/UPDATE sem WHERE:** ao executar (F5), se o trecho tiver `DELETE` ou `UPDATE` sem `WHERE`, ou `TRUNCATE TABLE`, o QueryPilot lista as instruções (com a linha) e pergunta antes; "Não" cancela a execução. Não alerta para tabelas temporárias e variáveis de tabela, `UPDATE ... FROM ... INNER JOIN`, `MERGE`, nem para texto em comentários e strings.
- **Abas fechadas:** ao fechar uma janela de query (ou o SSMS), o texto e a conexão ficam guardados. `Ctrl+Q, Ctrl+T` reabre a última, conectada ao mesmo servidor e banco; repetindo, reabre as anteriores. *QueryPilot › Abas fechadas...* mostra todas, com busca (é a janela do histórico com "Mostrar: Abas fechadas").
- Os dois podem ser desligados em *Opções*.

## Formatador de SQL (v0.4)

`Ctrl+K, Ctrl+Y` (ou *QueryPilot › Formatar SQL*) formata a seleção (linhas inteiras, mantendo o recuo) ou, sem seleção, a janela inteira. Um `Ctrl+Z` desfaz.

```sql
SELECT
    c.Name,
    o.OrderID
FROM dbo.Customer c
INNER JOIN dbo.[Order] o
    ON o.CustomerID = c.CustomerID
WHERE c.Active = 1
    AND o.Total > 100
ORDER BY c.Name
```

- Uma coluna por linha no SELECT; cláusulas, JOINs, `ON` e `AND`/`OR` em linhas próprias.
- Subconsultas e `CASE` longos recuados; os curtos (até 60 caracteres) ficam numa linha.
- `IF/ELSE`, `BEGIN/END`, `TRY/CATCH`, `CREATE PROCEDURE` (um parâmetro por linha), `CREATE TABLE` (uma coluna por linha), CTEs, `MERGE`, `GO`.
- **Comentários preservados**, inclusive no meio da instrução; uma linha em branco do original continua.
- Só mexe em espaços, quebras de linha e na caixa das palavras-chave (nomes de tabela/coluna nunca mudam). Antes de aplicar, o resultado é comparado token a token com o original; se algo diferir, ou se houver string/comentário sem fechamento, nada é alterado e a barra de status explica.
- *Opções*: palavras-chave em MAIÚSCULAS, minúsculas ou como estão; recuo de 4, 2 espaços ou tabulação; vírgula no fim ou no começo da linha.

## v0.1: o que faz

- **Histórico de queries.** Cada execução no editor (F5, Execute ou Ctrl+E) é gravada com o texto executado (só o trecho selecionado, se houver seleção), servidor, banco, login, data/hora, duração, linhas afetadas e status (OK, Erro ou Cancelada).
- **Janela de histórico** (`Ctrl+Q, Ctrl+E`, ou menu *QueryPilot › Histórico de queries*):
  - filtros por período, banco, servidor e "só com erro";
  - busca no texto da query (`Ctrl+F`);
  - preview do SQL;
  - **Abrir em nova aba** (Enter ou duplo clique) abre a query já conectada no mesmo servidor e banco;
  - **Copiar** (`Ctrl+C`).
- **Cor da aba por servidor.** Cada regra liga um padrão de nome de servidor (com `*` e `?`) a uma cor, por exemplo `PROD-*` → vermelho. As abas das queries conectadas nesses servidores ficam com um tom da cor e uma faixa embaixo.
- **Opções** (*QueryPilot › Opções...*): ligar e desligar a captura, dias de retenção (padrão 90; 0 = para sempre), ligar e desligar as cores, e editar as regras de cor. O botão "Adicionar servidor atual" cria uma regra para o servidor da query ativa.

O menu **QueryPilot** fica na barra principal do SSMS e também em *Tools › QueryPilot*.

Os dados ficam em `%LOCALAPPDATA%\QueryPilot\`:
- `history.db`: SQLite;
- `settings.json`;
- `logs\`: guardados por 14 dias.

Nenhuma senha é gravada. Para reabrir uma query com login SQL, a extensão reaproveita a conexão da sessão atual do SSMS; com autenticação Windows, ela monta a conexão de novo.

## Instalar e desinstalar

Requisitos: SSMS 22 (x64). A instalação é por usuário e não precisa de administrador.

```powershell
.\build.ps1 -Install     # compila, testa, gera dist\ e instala no SSMS 22 do usuário atual
```

Também dá para usar direto `dist\Instalar.cmd` e `dist\Desinstalar.cmd`. Feche o SSMS antes de qualquer um dos dois.

## Trocar o nome do produto

Edite **somente** `Product.props` (`ProductName`, `ProductPublisher`, `ProductDescription`) e rode `.\build.ps1 -Install`. O novo nome aparece no menu, nos títulos das janelas, nos comandos e no gerenciador de extensões.

`ProductId` **não** deve mudar: é ele que faz a versão nova substituir a instalada. Se mudar `DataFolderName`, a extensão copia os dados da pasta anterior na primeira vez que carregar (`AppPaths.MigrateFrom`).

O código interno (solução, projetos e namespaces) usa o nome de trabalho `SqlTools` e não depende do nome do produto.

## Desenvolvimento

- C# / .NET Framework 4.8, VSIX para o SSMS 22 (shell VS 18, x64). Build com o MSBuild do Visual Studio; o SDK vem pelo NuGet (`Microsoft.VSSDK.BuildTools`), sem precisar do workload de extensões.
- `src/SqlTools.Core`: histórico (SQLite), configurações e regras. Sem dependência do Visual Studio; testado em `tests/`.
- `src/SqlTools.Ssms`: o pacote.
  - **Todo acesso a internals do SSMS fica em `Integration/SsmsBridge.cs`**, via reflection. Se uma atualização do SSMS mudar algo, o recurso afetado fica indisponível e o problema vai para o log, sem derrubar o SSMS.
- Pontos de integração usados:

| O quê | Como |
|---|---|
| Momento da execução | `CommandEvents` do comando Query.Execute `{52692960-56BC-4989-B5D3-94C47A513E8D}:1` |
| Fim, duração e linhas | evento interno `DisplaySQLResultsControl.ScriptExecutionCompleted` e `RowsAffected` |
| Conexão da janela | `ScriptAndResultsEditorControl.Connection` / `m_connection` |
| Abrir query conectada | `ServiceCache.ScriptFactory.CreateNewBlankScript(ScriptType.Sql, UIConnectionInfo, null)` |
| Aba → janela | `DocumentTabItem.View.WindowFrame` (cor desenhada com `Adorner`) |
| Editor SQL (MEF) | content type `SQL` (RadLangSvc); `IAsyncCompletionSource` + commit manager |
| Lista nativa | `LANGPREFERENCES3.fAutoListMembers` do language service `{c4d96929-a9b0-42cc-b3e0-adac0435d7f2}` |
| Conexão de metadados | `UIConnectionInfoUtil.GetCoreConnectionInfo` → `SqlConnectionInfo.CreateConnectionObject()` |
| Menu de topo visível | `Menus\AllowedExtensionMenus` no `privateregistry.bin` (`tools/Allow-TopMenu.ps1`) |

### Teste de integração no SSMS real

```powershell
.\build.ps1 -Install
.\tools\Run-SelfTest.ps1      # precisa do LocalDB; usa pasta de dados isolada
```

O teste cria o banco `QueryPilotSelfTest` no LocalDB (tabelas com FKs, outro schema, uma procedure), abre o SSMS conectado em `(localdb)\MSSQLLocalDB` e verifica: autocomplete (JOIN pela FK, colunas do alias, expandir `*`, procedures, schema, `sys.`, digitação real com Tab, lista nativa fechada), execução com sucesso, seleção parcial, execução com erro, comando e atalho, cor das abas, janela de histórico, reabrir conectado e janela de opções. O resultado sai em `_scratch\selftest\result.txt`, com imagens das janelas em `_scratch\selftest\*.png`.

## Limitações conhecidas

- A Microsoft não suporta oficialmente extensões de terceiros no SSMS 22. Depois de atualizar o SSMS, rode o self-test.
- Na primeira execução, o QueryPilot assume o atalho `Ctrl+Q, Ctrl+E` uma única vez, mesmo que outra extensão já o use. Depois disso, qualquer troca feita em *Tools › Options › Keyboard* é respeitada.

## Licença

MIT. Veja [LICENSE](LICENSE).
