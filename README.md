# Memento 2.1

Aplicativo local de notas para Windows 10/11 em C# / Windows Forms, .NET Framework 4.8. Sem NuGet, navegador embutido ou rede.

## Compilar

Execute na pasta do código:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1 -RunTests
```

O script usa o compilador do .NET Framework em %WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe, com alternativa para Framework de 32 bits. Não é necessário baixar SDK ou Visual Studio. As saídas são build\Memento.exe e build\Memento-Instalar.exe. O projeto Memento.csproj pode ser aberto no Visual Studio com o pacote de desenvolvimento do .NET Framework 4.8.

Para a revisão visual de teste, adicione -VisualTest. Isso compila Memento.VisualTest.exe com notas em um suporte de revisão e dados isolados. Esse suporte não faz parte do aplicativo instalado.

## Mudanças da versão 2.0

- Largura fixa de 320 unidades a 96 DPI. Sem dimensionamento manual.
- Grade de colunas com espaçamento de 20 unidades e margens de 24. Até cinco colunas em 1920 × 1080 a 100%, considerando a área útil.
- Notas comuns começam em 300 unidades e crescem com o texto. A distribuição conserva a altura das outras notas, empurra as inferiores e limita o crescimento antes da borda da tela.
- Rolagem vertical apenas quando o conteúdo não cabe na altura disponível. Textos mais curtos recolhem as notas.
- Títulos de coluna com 150 unidades, texto centralizado e em negrito.
- Tema padrão global para novas notas e títulos, nomes de temas personalizados e persistentes.
- Proteção de Mostrar área de trabalho com exclusão de Aero Peek, ausência de minimização e observação da ativação do shell, sem capturar Win+D ou alterar atalhos.
- Novo nome Memento e crédito discreto Criado por Igor Pradella na bandeja.
- Migração do Pingo, preservando o arquivo original de notas.

A remoção do rodapé foi concluída na versão 2.1; o pedido original está em PROXIMAS-ATUALIZACOES.md.

## Arquitetura

App.cs controla o ciclo de vida pela bandeja, a organização das notas, a importação/exportação e os temas. Não existe janela principal obrigatória. NoteForm.cs contém o editor de texto simples e as opções da nota. GridLayout.cs separa os cálculos de grade e distribuição vertical da interface. Models.cs grava JSON com troca atômica, cópia anterior e recuperação. ThemeSettingsForm.cs edita nomes e padrão dos temas. DesktopSupport.cs observa a ativação do shell e configura as próprias janelas para Aero Peek. Shared.cs contém caminhos, inicialização opcional, DPI e comunicação entre instâncias.

## Dados e migração

Formato JSON versão 2; versão 1 é aceita e normalizada. Dados instalados em %LOCALAPPDATA%\Memento. Na versão portátil, um marcador Memento.portable ativa a subpasta Dados ao lado do executável. Não rode a mesma pasta portátil em computadores simultâneos.

O instalador encerra as instâncias normais do Memento e do Pingo com solicitação de salvamento. Antes de substituir o programa antigo, valida e copia as notas do Pingo para a nova pasta, salvo se já houver dados do Memento. O original do Pingo permanece intacto. Falha de migração interrompe a instalação antes de substituir o programa anterior. Os binários anteriores do Memento são restaurados se falhar a cópia do payload. Atalhos, inicialização e registro da versão anterior são ajustados após a instalação.

O salvamento usa arquivo temporário, Flush(true) e File.Replace, mantendo notas.json.bak. Um JSON danificado é preservado antes da recuperação. Importar uma cópia acrescenta notas; não troca os temas globais atuais. Quando não há espaço suficiente, notas importadas excedentes ficam guardadas na bandeja, com aviso.

A posição é a combinação de monitor, coluna e ordem. Coordenadas negativas e escala do monitor são consideradas. Desconectar um monitor transfere temporariamente a grade para a tela disponível; mover uma nota grava seu novo monitor. Notas guardadas e lixeira não ocupam a grade.

## Extração sem instalar

```powershell
.\build\Memento-Instalar.exe --extract .\payload-extraido
```

Isso não registra o aplicativo nem cria atalhos. O instalador normal é por usuário e não solicita administrador. A desinstalação preserva os dados e deixa o próprio Desinstalar.exe, que pode ser removido após seu encerramento.

## Verificação e limites

Tests.cs cobre dados, recuperação, migração, temas, grade, crescimento, compressão, rolagem e estilos das notas. O relatório da entrega distingue os testes executados da revisão visual. Win+D foi conferido manualmente pelo usuário na versão 2.1. O uso físico de dois monitores com escalas distintas ainda requer confirmação. O suporte visual usa controles reais, mas não reproduz a ordem das janelas do Explorer.

O aplicativo limita o crescimento à capacidade da coluna e não sobrepõe notas. Quando a tela inteira estiver cheia, será necessário guardar ou mover notas para abrir espaço. Notas curtas são o uso recomendado. O código não contém sincronização, formatação de texto ou criptografia própria.

## APIs de referência

- [Bandeja](https://learn.microsoft.com/en-us/dotnet/api/system.windows.forms.notifyicon?view=netframework-4.8.1)
- [ShowInTaskbar](https://learn.microsoft.com/dotnet/api/system.windows.forms.form.showintaskbar)
- [Atributos DWM, incluindo exclusão de Aero Peek](https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/ne-dwmapi-dwmwindowattribute)
- [Comportamento de Mostrar área de trabalho](https://devblogs.microsoft.com/oldnewthing/20241021-00/?p=110393)

Licença MIT. Aplicativo e instalador sem assinatura digital.

## Correções da versão 2.1

A nota comum começa com 320 × 300 px a 96 DPI, e o título com 320 × 150 px. A altura só cresce depois de o texto ocupar o espaço normal. A largura permanece fixa.

O limite MaximumSize tem valores positivos nas duas dimensões. A versão anterior usava altura zero junto à largura fixa, o que pode colapsar uma janela real no .NET Framework. Os testes agora criam o identificador nativo e verificam GetWindowRect, corpo de edição e botões, além dos cálculos de grade.

Os botões são posicionados explicitamente por DPI; o rodapé foi removido. Falhas ao salvar continuam sendo informadas. A criação usa a última nota ativa e insere abaixo dela; se a coluna estiver cheia, procura a vizinha.

A proteção da área de trabalho eleva temporariamente as próprias notas ao nível topmost quando o Explorer mostra o desktop. Ela volta ao nível normal ao ativar outro aplicativo e preserva o alfinete escolhido pelo usuário. Não captura atalhos e não altera configurações do Windows.

Para conferir janelas reais, compile com -NativeReview e abra Memento.NativeReview.exe. O controlador usa uma pasta isolada RevisaoNativa, mantém as notas como janelas independentes e registra somente suas dimensões/estilos. O teste de Win+D é manual.

A versão 2.1 passou em 67 verificações automatizadas, na conferência de quatro notas reais em duas colunas e no teste de Win+D confirmado pelo usuário.
