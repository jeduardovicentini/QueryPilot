# Paridade de funcionalidades — extensão SSMS (SqlTools)

Data: 2026-11-06 · Alvo: SSMS 22.x (shell VS 2022, 64-bit)

## Concorrentes analisados

| Produto | Fornecedor | Modelo | Situação 2026 |
|---|---|---|---|
| SQL Prompt | Redgate | ~US$210/usuário/ano | Líder. SSMS 18–22, VS 2019–2026. AI (Redgate Assistant, Explain/Fix/Optimize) |
| dbForge SQL Complete | Devart | Express grátis / Standard / Pro | Mais completo em recursos. SSMS 22.6 suportado (mai/2026). AI Assistant |
| ApexSQL Complete | Quest | Grátis | Linha ApexSQL sendo absorvida pelo Toad. Estagnado |
| SSMSBoost | Solutions Crew | Community grátis / Pro | Foco em produtividade, histórico, sessões |
| SSMS Tools Pack | Mladen Prajdić | Pago barato | Histórico de execução + histórico de janelas |
| Schema compare | Redgate SQL Compare, dbForge Schema Compare, ApexSQL Diff | Produtos separados | Referência para o módulo de comparação |

## Atenção: o SSMS 22 já faz parte disso nativamente

| Recurso nativo | Versão | Impacto |
|---|---|---|
| IntelliSense T-SQL | sempre | Fraco: sem JOIN automático, sem fuzzy, cache lento. Ainda há espaço |
| SQL Formatter | preview em 22.7, GA em 22.10.2 | Muitas opções (vírgula à frente, case, JOIN/ON, IN, colchetes). Formatter próprio precisa ser **melhor**, não só existir |
| Schema Compare (DB, .dacpac, SQL project) | preview em 22.7, GA em 22.10.2 | Comparação básica resolvida. Diferencial tem que vir de outra coisa |
| GitHub Copilot (chat, completions, Agent Mode, MCP) | GA em 22.4.1 / 22.10.2 | Competir em AI genérica não compensa. Melhor integrar com o que já existe |
| Exportar grid para Excel/JSON/Markdown/XML | 22.4.1 | Já resolvido |

O usuário está no **22.7.0**: formatter e schema compare ainda estão em preview. Testar no 22.11.x.

Suporte oficial a extensões: a Microsoft **não suporta oficialmente** extensões de terceiros no SSMS 22, mas também não bloqueia. Instalação via `VSIXInstaller.exe` do SSMS ou cópia para a pasta `Extensions`. Atualizações do SSMS já quebraram extensões (22.3). **Risco técnico nº 1.**

## Matriz de paridade

Legenda: ● tem · ◐ parcial · — não tem · **P0** obrigatório no MVP · **P1** paridade · **P2** diferencial/depois

### 1. Autocomplete / IntelliSense

| Recurso | Prompt | dbForge | Apex | SSMS nativo | Prioridade |
|---|---|---|---|---|---|
| Sugestão de objetos por contexto (tabela, coluna, schema, função, proc, variável, alias) | ● | ● | ● | ◐ | **P0** |
| Cache de metadados por conexão + refresh manual/automático | ● | ● | ● | ◐ | **P0** |
| Expansão de `SELECT *` em colunas (Tab) | ● | ● | ● | — | **P0** |
| Sugestão de JOIN completo via FK (`ON a.x = b.y`) | ● | ● | ● | — | **P0** |
| INSERT completo com lista de colunas/VALUES | ● | ● | ● | — | **P1** |
| Fuzzy matching / CamelCase (`CuOr` → `CustomerOrders`) | ● | ● | ◐ | — | **P1** |
| Aliases automáticos de tabela | ● | ● | ● | — | **P1** |
| Parâmetros de proc/função (EXEC com params) | ● | ● | ● | ◐ | **P1** |
| Colunas de CTE, tabela derivada, temp table, table variable | ● | ● | ◐ | ◐ | **P1** |
| Tooltip/Quick info do objeto (colunas, tipos, DDL, extended properties) | ● | ● | ● | ◐ | **P1** |
| Sugestões entre bancos / servidores vinculados | ● | ● | ◐ | ◐ | **P2** |
| Ordenação por frequência de uso | ● | ● | — | — | **P2** |
| Liga/desliga (Ctrl+Shift+A como no Apex) | ● | ● | ● | ● | **P0** |

### 2. Formatação

| Recurso | Prompt | dbForge | SSMS nativo | Prioridade |
|---|---|---|---|---|
| Formatar documento/seleção (atalho) | ● | ● | ● | **P0** |
| Perfis de estilo configuráveis + preview | ● | ● | ● | **P0** |
| Exportar/compartilhar perfil (arquivo no repo) | ● | ● | — | **P1** |
| Case de keywords/funções/tipos/identificadores | ● | ● | ● | **P0** |
| Preservar comentários e SQLCMD com segurança | ● | ● | ◐ (bugs recentes) | **P0** |
| Formatar na digitação / ao executar | ◐ | ● | — | **P2** |
| Formatação em lote (arquivos/objetos do banco) | ● | ● | — | **P2** |
| "Unformat"/minificar | — | ● | — | **P2** |
| Bloquear trechos com marcador (`-- format off`) | ● | ● | — | **P1** |

### 3. Snippets e auto-replace

| Recurso | Prompt | dbForge | Apex | Prioridade |
|---|---|---|---|---|
| Biblioteca de snippets (ex.: `ssf` → `SELECT * FROM`) | ● | ● | ● (200+) | **P0** |
| Placeholders/variáveis no snippet ($CURSOR$, data, usuário) | ● | ● | ● | **P1** |
| Auto-replacements (texto → texto ao digitar) | ◐ | ● | ● | **P1** |
| Compartilhamento de snippets em equipe | ● | ● | — | **P2** |

### 4. Histórico de queries e sessão

| Recurso | Prompt | dbForge | Apex | SSMSBoost | Tools Pack | Prioridade |
|---|---|---|---|---|---|---|
| Log de toda query executada (texto, servidor, banco, usuário, data, duração, status) | ● | ● | ● | ● | ● | **P0** |
| Busca full-text + filtro por data/servidor/banco | ● | ● | ● | ● | ● | **P0** |
| Reabrir query do histórico em nova aba | ● | ● | ● | ● | ● | **P0** |
| Retenção configurável / limpeza automática | ● | ● | ◐ | ● | ● | **P1** |
| Histórico de abas fechadas (Tab History) | ● | ● | ◐ | ● | ● | **P0** |
| Snapshot periódico do conteúdo das janelas (não executado) | ● | ● | — | ● | ● | **P1** |
| Restaurar workspace após crash/fechar | ● | ● | ● | ● | ● | **P1** |
| Armazenar em banco central (SQL Server) além de local | — | — | — | ◐ | ● | **P2** |

### 5. Navegação e segurança de ambiente

| Recurso | Prompt | dbForge | Apex | SSMSBoost | Prioridade |
|---|---|---|---|---|---|
| Cor da aba / status bar por servidor/banco (prod em vermelho) | ● | ● | ● | ● | **P0** |
| Alerta antes de executar DELETE/UPDATE sem WHERE, DROP, TRUNCATE | ● | ● | ◐ | ● | **P0** |
| Alerta extra em ambiente "produção" | ◐ | ● | — | ● | **P1** |
| Go to definition / Go to object (localizar no Object Explorer) | ● | ● | ● | ● | **P1** |
| Code structure / outline do script | ● | ● | ● | — | **P1** |
| Navegação entre abas (switcher) | ● | ● | ● | ● | **P2** |
| Executar só o statement atual | ● | ● | — | ● | **P1** |
| "Test mode" (executa em transação e faz rollback) | — | — | ● | — | **P2** |
| Agrupar procs/views/funções por schema no Object Explorer | ● | — | — | — | **P2** |

### 6. Análise de código e refatoração

| Recurso | Prompt | dbForge | Prioridade |
|---|---|---|---|
| Code analysis em tempo real (regras tipo `SELECT *`, NOLOCK, sem schema, variável não usada) | ● (~100 regras) | ● | **P1** |
| Rename inteligente (objeto, alias, variável) com script de dependências | ● | ● | **P2** |
| Qualificar nomes de objetos (adicionar schema) | ● | ● | **P1** |
| Encontrar objetos inválidos | ● | ● | **P2** |
| Gerar script CRUD / EXEC de proc com parâmetros | ● | ● | **P2** |
| Highlight de identificador e de BEGIN/END | ● | ● | **P1** |

### 7. Resultados (grid)

| Recurso | Prompt | dbForge | SSMSBoost | Prioridade |
|---|---|---|---|---|
| Agregados da seleção (SUM/AVG/COUNT/MIN/MAX) | — | ● | ● | **P1** |
| Busca dentro do grid | ◐ | ● | ● | **P1** |
| Copiar como INSERT / IN-list / CSV / JSON | ● | ● | ● | **P1** |
| Visualizar valor grande (XML/JSON/texto) | — | ● | ● | **P2** |

### 8. Schema Compare (módulo à parte)

Base: Redgate SQL Compare, dbForge Schema Compare, ApexSQL Diff e o nativo do SSMS 22.10+.

| Recurso | Concorrentes | SSMS nativo | Prioridade |
|---|---|---|---|
| Fontes: DB↔DB, DB↔.dacpac, DB↔pasta de scripts, DB↔SQL project | ● | ● (sem pasta de scripts) | **P0** |
| Árvore de diferenças agrupada (tipo/schema/ação) + diff lado a lado | ● | ● | **P0** |
| Opções de ignorar (whitespace, comentários, colação, permissões, ordem de colunas, nomes de constraints) | ● | ● | **P0** |
| Gerar script de sync com dependências ordenadas + transação | ● | ● | **P0** |
| Avisos de perda de dados (drop coluna, mudança de tipo) | ● | ◐ | **P1** |
| Mapeamento manual de tabelas/colunas/schemas renomeados | ● (dbForge) | — | **P2** |
| Snapshots de schema versionados | ● | ◐ (dacpac) | **P1** |
| CLI para CI/CD | ● (dbForge, Redgate) | ◐ (SqlPackage) | **P2** |
| Comparação de **dados** (Data Compare) | produto separado | — | **P2 — diferencial real** |

Recomendação: **não construir motor de diff do zero.** Usar `Microsoft.SqlServer.DacFx` (`SchemaComparison`), o mesmo do SSMS/SqlPackage. Diferencial = pasta de scripts como fonte, filtros salvos, avisos de perda de dados, histórico de comparações, Data Compare.

### 9. AI

Redgate e dbForge já têm (Explain, Fix, Optimize, Text-to-SQL, chat). O SSMS tem Copilot com Agent Mode e MCP. **P2**. Se fizer, que seja uma ação contextual usando os metadados já em cache (explicar plano, sugerir índice), não um chat concorrente.

## Recomendação de MVP (ordem)

1. **Fundação**: parser T-SQL (`Microsoft.SqlServer.TransactSql.ScriptDom`, já vem no SSMS) + cache de metadados por conexão. Todo o resto depende disso.
2. **Autocomplete P0**: contexto, `*`→colunas, JOIN via FK, liga/desliga.
3. **Histórico P0**: log de execução + busca + reabrir + abas fechadas. Barato e muito valorizado. Armazenamento em SQLite local.
4. **Segurança P0**: cor por ambiente + alerta de DELETE/UPDATE sem WHERE.
5. **Formatter**: ScriptDom `Sql160ScriptGenerator` como base, com regras próprias por cima.
6. **Snippets**.
7. **Schema Compare** com DacFx.

## Riscos técnicos

- SSMS 22 sem suporte oficial a extensões: testar cada release (22.x muda rápido). Manter uma matriz de versões testadas.
- Hook de execução de query: o SSMS não expõe API pública estável; concorrentes usam eventos de comando (`Query.Execute`) + reflection nos serviços internos. Isolar isso numa camada de adaptação.
- Conflito com IntelliSense nativo e com Copilot inline suggestions: oferecer opção de desativar o nativo.
- Performance do cache de metadados em bancos grandes (10k+ objetos): carregar assíncrono e incremental.

## Fontes

- Redgate SQL Prompt: https://www.red-gate.com/products/sql-prompt/
- Redgate Prompt AI: https://documentation.red-gate.com/sp/ai-in-sql-prompt/prompt-ai-features
- dbForge SQL Complete releases: https://www.componentsource.com/product/dbforge-sql-complete/releases
- dbForge Productivity Extension: https://www.devart.com/de/dbforge/sql/sqlcomplete/productivity-extension.html
- SSMSBoost vs dbForge: https://www.devart.com/blog/ssmsboost-addin-for-ssms-vs-sql-complete.html
- ApexSQL Complete FAQ: https://apexsql.com/faqs/faqs_complete.aspx
- SSMS add-ins (SSMS Tools Pack, SSMSBoost): https://www.sqlservercentral.com/articles/sql-server-management-studio-add-ins
- Schema compare (matriz Devart): https://www.devart.com/dbforge/sql/schemacompare/competitor-comparison.html
- Schema compare tools 2026 (Bytebase): https://www.bytebase.com/blog/top-sql-server-schema-compare-tools/
- SSMS 22 release notes: https://learn.microsoft.com/en-us/ssms/release-notes-22
- SSMS FAQ (extensões): https://learn.microsoft.com/en-sg/ssms/faq
