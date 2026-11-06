# QueryPilot

Extensão gratuita e de código aberto para o **SQL Server Management Studio 22**.

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
| Menu de topo visível | `Menus\AllowedExtensionMenus` no `privateregistry.bin` (`tools/Allow-TopMenu.ps1`) |

### Teste de integração no SSMS real

```powershell
.\build.ps1 -Install
.\tools\Run-SelfTest.ps1      # precisa do LocalDB; usa pasta de dados isolada
```

O teste abre o SSMS conectado em `(localdb)\MSSQLLocalDB` e verifica: execução com sucesso, seleção parcial, execução com erro, comando e atalho, cor das abas, janela de histórico, reabrir conectado e janela de opções. O resultado sai em `_scratch\selftest\result.txt`, com imagens das janelas em `_scratch\selftest\*.png`.

## Limitações conhecidas

- A Microsoft não suporta oficialmente extensões de terceiros no SSMS 22. Depois de atualizar o SSMS, rode o self-test.
- Na primeira execução, o QueryPilot assume o atalho `Ctrl+Q, Ctrl+E` uma única vez, mesmo que outra extensão já o use. Depois disso, qualquer troca feita em *Tools › Options › Keyboard* é respeitada.

## Licença

MIT. Veja [LICENSE](LICENSE).
