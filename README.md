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
| Cor por comentário | No diálogo, escolha entre 6 cores da paleta; o realce, o marcador da margem, o texto inline e a linha no painel usam a cor escolhida |
| Destaque no código | O trecho comentado fica realçado (cada cor é editável em *Tools > Options > Environment > Fonts and Colors*, itens **Local Comments Highlight - \<cor\>**) |
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
| `get_comments` | Comentários com texto, cor, linha (base 1) e o trecho de código ancorado |
| `search_comments` | Busca textual, para trazer só as anotações de um assunto |
| `write_documentation` | Grava o Markdown final ao lado do arquivo de comentários |

### Prompts prontos

Em *chat > + Add Reference > Prompts > MCP prompts*:

- **`generate_documentation`** — lê os comentários, confere contra o código-fonte e gera um
  `DOCUMENTATION.md` com diagrama Mermaid em bloco ```mermaid.
- **`review_open_questions`** — transforma TODOs e dúvidas anotadas em lista priorizada de ações.

### Instalação — nenhuma

O servidor vai **dentro do VSIX**, na pasta `MCP\`, e a própria extensão o registra: ao abrir uma
solução, o package escreve a entrada em `<SolutionDir>\.vs\mcp.json`, um dos locais que o
[Visual Studio varre](https://learn.microsoft.com/en-us/visualstudio/ide/mcp-servers?view=visualstudio)
em busca de configuração MCP. Nada de publish, nada de configurar na mão.

```json
{
  "servers": {
    "local-comments": {
      "type": "stdio",
      "command": "C:\\...\\Extensions\\<id>\\MCP\\LocalComents.Mcp.exe",
      "args": ["--file", "C:\\Repo\\MeuProjeto\\.local-comments.json"]
    }
  }
}
```

Os dois valores são resolvidos em tempo de execução, e é justamente por isso que o arquivo é
escrito pelo package em vez de ir pronto dentro do VSIX:

- **`command` é absoluto.** A pasta de instalação da extensão não está no `PATH`, então um nome
  simples como `LocalComents.Mcp.exe` não resolveria.
- **`--file` aponta para o arquivo real.** Sem ele o servidor sobe diretórios a partir do
  *working directory* do processo, que o Visual Studio não garante ser a pasta da solução — o
  resultado seria ler o arquivo errado e responder "nenhum comentário", sem erro nenhum.

A entrada é mesclada no `.vs\mcp.json`: outros servidores configurados ali são preservados, e o
arquivo só é reescrito quando algo de fato muda (salvar reinicia o agent do Copilot). Desligando
*Register the MCP server for this solution* nas opções, a entrada é removida e o arquivo volta ao
que era. `.vs\` já é ignorado pelo Git por convenção, então nada disso entra no repositório.

#### Ciclo de vida da entrada

O `command` aponta para dentro da pasta de instalação da extensão, então a entrada não pode
sobreviver à extensão — o Visual Studio ficaria tentando subir um executável que não existe mais.
Como nada do nosso código roda depois de uma desinstalação, a limpeza acontece antes:

- **Ao fechar o Visual Studio**, o package remove a entrada de todos os `.vs\mcp.json` em que
  escreveu na sessão. A próxima abertura da solução escreve de volta, idêntica — o *trust baseline*
  que o VS guarda para o servidor não é perturbado.
- **Ao abrir uma solução**, se o executável não for encontrado ao lado da extensão, a entrada é
  **removida** em vez de mantida. É a auto-cura para o caso de o VS ter sido fechado à força e não
  ter passado pela limpeza acima.

Sobra um caso que a extensão não tem como cobrir: fechar o VS de forma anormal **e** desinstalar em
seguida. A entrada órfã fica, e o VS mostra aquele servidor como falho. É inerte para o resto —
apagar a pasta `.vs\` resolve.

Depois de instalar, é só ativar as tools no ícone de chave inglesa do chat em modo *Agent* — elas
vêm desabilitadas por padrão, comportamento do VS para qualquer servidor MCP.

Rodando o servidor à mão (Claude Code, por exemplo), a resolução do arquivo é:
`--file <caminho>`, depois a variável `LOCALCOMENTS_FILE`, depois um *walk-up* a partir do
diretório de trabalho, e por fim o perfil do usuário.

#### Por que `net472` e não `net8.0`

O servidor roda **fora** do `devenv.exe`, então o isolamento de processo já resolveria o conflito
de binding de assembly do `System.Text.Json`. O motivo real é outro: o Visual Studio garante
.NET Framework 4.7.2, enquanto o runtime do .NET 8 seria um pré-requisito que a extensão não tem
como instalar. Todo o grafo de dependências do `ModelContextProtocol` 2.1.0 publica
`netstandard2.0`/`net462`, então o alvo é viável.

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

- **Save location**: `Solution` (padrão), `User` (perfil do usuário) ou `Custom`. No modo `Solution` a
  raiz é a pasta do `.sln`/`.slnx` — ou a pasta aberta, quando se usa *Open Folder* (Folder View).
  Sem nada aberto, cai no perfil do usuário
- **Custom folder**: pasta usada quando o modo é `Custom`
- **File name**: padrão `.local-comments.json`
- **Show glyph in the margin** / **Highlight commented code** / **Show comment text inline**: liga e desliga cada indicador visual
- **Hide stale comments**: esconde comentários cujo código âncora não existe mais
- **Register the MCP server for this solution**: escreve (ou remove) a entrada do servidor em
  `<SolutionDir>\.vs\mcp.json`

> Dica: adicione `.local-comments.json` ao `.gitignore` se as anotações forem pessoais.

### Formato do arquivo

Idêntico ao da extensão do VS Code (linhas e colunas **base zero**):

```json
{
  "C:\\Repo\\MeuProjeto\\Program.cs": [
    {
      "id": "a1b2c3",
      "text": "Isso aqui precisa de refactor",
      "color": "red",
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

#### O campo `color`

Guarda um identificador da paleta — `yellow`, `orange`, `red`, `green`, `blue` ou `purple` — e não
um valor RGB, para que a cor continue fazendo sentido depois de trocar de tema ou de editar a
paleta em *Fonts and Colors*. Um identificador desconhecido cai no padrão em vez de descartar o
comentário.

A propriedade é **omitida** quando a cor é a padrão (`yellow`), então arquivos criados antes desta
versão continuam byte a byte iguais.

> Este é o único ponto em que o formato se afasta do da extensão do VS Code. Ela ignora a
> propriedade ao ler, mas **descarta** a cor se reescrever aquele comentário. Anotar pelos dois
> editores no mesmo arquivo continua funcionando; só a cor não sobrevive a uma edição feita pelo
> VS Code.

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

`Services/McpServerRegistration.cs` é o que conecta os dois mundos: roda dentro do package, onde os
dois dados que o servidor precisa — o caminho do executável e o do arquivo de comentários — já são
conhecidos, e os grava no `.vs\mcp.json` da solução.

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

O workload *Visual Studio extension development* é dispensável para compilar: o
`Microsoft.VSSDK.BuildTools` traz as ferramentas pelo NuGet e o import dos targets de design-time
é condicionado a `Exists()`.

### CI

[`.github/workflows/build.yml`](.github/workflows/build.yml) compila em `windows-latest`, publica o
`.vsix` como artifact e **confere o conteúdo do pacote**: um VSIX sem as DLLs do servidor MCP
instala normalmente e só quebra na hora de subir o servidor, então o build falha cedo se
`MCP\LocalComents.Mcp.exe` e suas dependências não estiverem lá.

## Limitações conhecidas

- Os comentários são ancorados por número de linha. Editar o arquivo fora do Visual Studio
  desalinha as marcações; a detecção de "stale" usa `selectedText` para sinalizar isso.
- Enquanto o documento é editado, as posições só são recalculadas ao salvar/recarregar o JSON.
- Arquivos ainda não salvos (`Untitled`) não podem receber comentários, pois a chave é o caminho.
