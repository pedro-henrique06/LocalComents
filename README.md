# Local Comments para Visual Studio

Anotações privadas no código, sem tocar nos arquivos-fonte. É a adaptação da extensão
[local-comments](https://github.com/marcelrsoub/local-comments) (VS Code) para o Visual Studio 2022/2026.

Os comentários ficam em um arquivo JSON **no mesmo formato da extensão do VS Code**, então o
mesmo `.local-comments.json` pode ser usado pelos dois editores no mesmo repositório.

## Funcionalidades

| Recurso | Como usar |
| --- | --- |
| Adicionar comentário | `Alt+C` no editor (ou botão direito → *Add Local Comment*) |
| Comentar seleção | Selecione o trecho e pressione `Alt+C`; sem seleção, comenta a linha inteira |
| Texto inline no editor | O comentário aparece no fim da linha anotada (`💬 texto`), como um inline hint, usando a cor de comentário do tema atual |
| Destaque no código | O trecho comentado fica realçado (cor editável em *Tools > Options > Environment > Fonts and Colors*, item **Local Comments Highlight**) |
| Marcador na margem | Um "balão" aparece na margem indicadora da linha; o tooltip mostra o texto |
| Tooltip ao passar o mouse | Quick Info mostra o comentário, a data e um aviso quando o código mudou |
| Painel lateral | *View > Other Windows > Local Comments* — busca, navegação, edição e exclusão |
| Sincronização externa | O arquivo JSON é monitorado; alterações feitas pelo VS Code aparecem automaticamente |

## Servidor MCP — documentação gerada por IA

O projeto `LocalComents.Mcp/` é um servidor [MCP](https://learn.microsoft.com/en-us/visualstudio/ide/mcp-servers?view=visualstudio)
que expõe os comentários locais como *tools*. Com ele, o **agent mode do Copilot no Visual Studio**
(ou o Claude Code, ou qualquer cliente MCP) lê suas anotações e escreve a documentação — a extensão
não embute nenhuma chamada de LLM nem gerencia API key.

### Ferramentas expostas

| Tool | O que faz |
| --- | --- |
| `list_files_with_comments` | Panorama: quais arquivos têm anotações e quantas |
| `get_comments` | Comentários com texto, linha (base 1) e o trecho de código ancorado |
| `search_comments` | Busca textual, para trazer só as anotações de um assunto |
| `write_documentation` | Grava o Markdown final ao lado do arquivo de comentários |

### Prompts prontos

Em *chat > + Add Reference > Prompts > MCP prompts*:

- **`generate_documentation`** — lê os comentários, confere contra o código-fonte e gera um
  `DOCUMENTATION.md` com diagrama Mermaid em bloco ```mermaid.
- **`review_open_questions`** — transforma TODOs e dúvidas anotadas em lista priorizada de ações.

### Instalação — nenhuma

O servidor vai **dentro do VSIX**, na pasta `MCP\`, e é registrado pelo asset:

```xml
<Asset Type="mcp.json" Path="MCP\mcp.json" />
```

Esse é o mesmo mecanismo que o servidor MCP embutido do NuGet usa
(`Common7\IDE\CommonExtensions\Microsoft\NuGet\MCP\`). O Visual Studio registra o servidor
**globalmente**, para qualquer solução — não é preciso `.mcp.json` por projeto, nem publish, nem
configuração manual. O `command` no [`MCP/mcp.json`](MCP/mcp.json) é relativo e resolve na pasta
onde o arquivo está.

Depois de instalar, é só ativar as tools no ícone de chave inglesa do chat em modo *Agent* — elas
vêm desabilitadas por padrão, comportamento do VS para qualquer servidor MCP.

O servidor localiza o arquivo de comentários subindo diretórios a partir do diretório de trabalho.
Para fixar explicitamente, use `--file <caminho>` ou a variável `LOCALCOMENTS_FILE`.

#### Por que `net472` e não `net8.0`

O servidor roda **fora** do `devenv.exe`, então o isolamento de processo já resolveria o conflito
de binding de assembly do `System.Text.Json`. Mas `net472` é o alvo certo por outro motivo: o
Visual Studio garante .NET Framework 4.7.2, enquanto o runtime do .NET 8 seria um pré-requisito que
a extensão não tem como instalar. O servidor MCP do NuGet é `net472` pelo mesmo motivo.

#### Armadilha no empacotamento

Os *output groups* do VSSDK removem do pacote as assemblies que o próprio Visual Studio
distribui — `Newtonsoft.Json`, `System.Text.Json`, `System.Memory` e outras 8. Isso é correto para
código carregado dentro do `devenv`, mas **quebra um processo separado**, que não resolve nada das
pastas do IDE. Por isso o [`LocalComents.csproj`](LocalComents.csproj) alimenta o `VSIXSourceItem`
diretamente com a saída do build do servidor, pelo target `AddMcpServerToVsix`, em vez de usar
`IncludeOutputGroupsInVSIX`. Sem isso o VSIX sai com 35 das 46 DLLs e o servidor falha ao subir na
máquina do usuário.

## Onde os comentários são gravados

*Tools > Options > Local Comments > General*:

- **Save location**: `Solution` (padrão, ao lado do `.sln`), `User` (perfil do usuário) ou `Custom`
- **Custom folder**: pasta usada quando o modo é `Custom`
- **File name**: padrão `.local-comments.json`
- **Show glyph in the margin** / **Highlight commented code** / **Show comment text inline**: liga e desliga cada indicador visual
- **Hide stale comments**: esconde comentários cujo código âncora não existe mais

> Dica: adicione `.local-comments.json` ao `.gitignore` se as anotações forem pessoais.

### Formato do arquivo

Idêntico ao da extensão do VS Code (linhas e colunas **base zero**):

```json
{
  "C:\\Repo\\MeuProjeto\\Program.cs": [
    {
      "id": "a1b2c3",
      "text": "Isso aqui precisa de refactor",
      "timestamp": 1755698292758,
      "range": {
        "startLine": 41,
        "startCharacter": 0,
        "endLine": 41,
        "endCharacter": 9007199254740991,
        "selectedText": "static void Main(string[] args)"
      }
    }
  ]
}
```

`endCharacter: 9007199254740991` (o `Number.MAX_SAFE_INTEGER` do JavaScript) significa
"até o fim da linha", convenção herdada do VS Code.

## Arquitetura

Mapeamento dos conceitos do VS Code para o Visual Studio:

| VS Code | Visual Studio | Arquivo |
| --- | --- | --- |
| `TextEditorDecorationType` | `ITagger<TextMarkerTag>` + `MarkerFormatDefinition` | `Editor/CommentHighlightTagger.cs` |
| Gutter icon | `IGlyphFactoryProvider` + `IGlyphTag` | `Editor/CommentGlyph.cs` |
| Inline hint / `after` decoration | `AdornmentLayerDefinition` + `IWpfTextViewCreationListener` | `Editor/InlineCommentAdornment.cs` |
| `HoverProvider` | `IAsyncQuickInfoSource` | `Editor/CommentQuickInfoSource.cs` |
| Sidebar (Webview) | `ToolWindowPane` + WPF | `ToolWindows/` |
| `contributes.commands` / `keybindings` | `.vsct` + `OleMenuCommandService` | `LocalComentsPackage.vsct`, `Commands/` |
| `workspace.getConfiguration` | `DialogPage` (Tools > Options) | `Options/LocalComentsOptionsPage.cs` |
| `globalState` / arquivo JSON | `CommentStore` + `FileSystemWatcher` | `Services/CommentStore.cs` |
| — (novo) | Servidor MCP para o agent mode | `LocalComents.Mcp/` |

O projeto MCP **linka** `Models/LocalComment.cs`, `Services/CommentStore.cs` e
`Services/LocalComentsLog.cs` do projeto VSIX em vez de duplicá-los, então o schema de
armazenamento tem uma fonte de verdade só. Apenas arquivos sem dependência do Visual Studio
podem ser compartilhados assim.

`CommentStore` é um singleton simples (não MEF) porque é compartilhado entre o *package*
(comandos, tool window) e os componentes MEF do editor, que são instanciados
independentemente. O package empurra as opções para `LocalComentsSettings`, já que os
taggers rodam fora da thread de UI e não conseguem ler o `DialogPage`.

## Compilar e depurar

```bash
"C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" LocalComents.csproj -t:Restore;Build
```

O VSIX sai em `bin\Debug\net472\LocalComents.vsix`. Para depurar, abra `LocalComents.slnx`
no Visual Studio e pressione `F5`: isso inicia a *Experimental Instance* com a extensão
carregada.

`Newtonsoft.Json` é referenciado apenas em tempo de compilação (`ExcludeAssets="runtime"`) —
o Visual Studio já distribui o assembly, então ele não vai dentro do VSIX.

## Limitações conhecidas

- Os comentários são ancorados por número de linha. Editar o arquivo fora do Visual Studio
  desalinha as marcações; a detecção de "stale" usa `selectedText` para sinalizar isso.
- Enquanto o documento é editado, as posições só são recalculadas ao salvar/recarregar o JSON.
- Arquivos ainda não salvos (`Untitled`) não podem receber comentários, pois a chave é o caminho.
