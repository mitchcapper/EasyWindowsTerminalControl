# EasyWindowsTerminalControl
A high performance full-feature WPF / (winui3* beta) terminal control that uses the [Official Windows Terminal](https://github.com/microsoft/terminal) console host for the backend driver.

It features full 24-bit color support with ANSI/VT escape sequences (and colors), hardware / GPU accelerated rendering, mouse support, and true console interaction.  This is not just some isolated window hosted in c#, you have full api control for input, output, sizing, and the terminal theming.  The control can start any console application you want (or any default terminal like powershell, pwsh, cmd.exe).  It provides XAML properties for easy settings, and events for things like input/output interception.   The control can also passively log the terminal session and provide both the raw terminal sequence (including all VT codes) and the standard UTF-8 human readable text.

The control features the ability to detach and re-attach live terminal instances between controls (even between windows). It also includes a basic support for the XAML designer (won't show theming).

Technically you could use the control simply as ANSI render frontend if you needed to display say log data or a prior saved console session in its original form.

<!-- MarkdownTOC -->

- [Usage](#usage)
- [Control Properties](#control-properties)
- [Methods](#methods)
	- [EasyTerminalControl](#easyterminalcontrol)
	- [TermPTY](#termpty)
- [Theming](#theming)
- [Limitations](#limitations)
- [Advanced Usage / Internals](#advanced-usage--internals)
- [ReadDelimitedTermPTY](#readdelimitedtermpty)

<!-- /MarkdownTOC -->

### Disclaimer
While the core of this control uses the Windows Terminal core control conpty/the wpf control are not yet publicly packaged.  This relies on beta packages so the low level API we use might change in the future. See [Issue #6999 · microsoft/terminal](https://github.com/microsoft/terminal/issues/6999) for details.

## Usage
`dotnet add package EasyWindowsTerminalControl`

```xml
<term:EasyTerminalControl StartupCommandLine="pwsh.exe" />
```

### WinUI3 Support
This control does support WinUI3 with a special package `EasyWindowsTerminalControl.WinUI`.  This is very alpha and very un-official.  Windows terminal does not have anything like a WinUI3 control and the dependency `CI.Microsoft.Terminal.WinUI3.Unofficial` is done in house to replicate the WPF package as closely as possible.  The biggest WinUI3 issue is the lack of HwndHost.  To make that package we created our own based on the original WPF version.  Otherwise the `EasyWindowsTerminalControl.WinUI` package replicates our `EasyWindowsTerminalControl` nearly identically for usage.  As this does use near identical implementations as the WPF version it has similar limitations as well.

### Control Properties
The control has all the standard UserControl properties but it has the following unique properties that support binding.  Note some of these properties are "write-only" meaning they are not meant to be read as there are external ways the properties could be changed we would be unaware of.  An example is the `IsCursorVisible` property where VT codes could change this (infact setting this property just sends VT codes itself).  Binding to a write only property means you won't get the change when it changes however you can always call control.IsCursorVisible=X and it will always update the terminal to that state.
- **StartupCommandLine** *(string, "powershell.exe")* - The full command line to launch for the application to run.  Can contain any arguments as well.
- **IsReadOnly** *(bool?, write only, null)* - Ignores all input from the Terminal GUI, this includes things like resize notifications so may have unintended affects.  It is actually a helper to setting it on the underlying ConPTYTerm.
- **IsCursorVisible** *(bool?, write only, null)* - Controls if the cursor is showing. Note apps emitting VT codes can re-enable it.
- **Theme** *(TerminalTheme?, write only)* - Sets the default terminal theme, background, foreground, cursor style, etc.
- **LogConPTYOutput** *(bool, false)* - If the underlying TermPTY instance should be told to log the application output to its buffer (can call ConPTYTerm.GetConsoleText() to retrieve)
- **Win32InputMode** *(bool, true)* - Standard VT sequences can't quite transmit all the key data that INPUT_RECORD could.  This uses a 'key record' sequence to transmit all the original information that conpty knows how to translate into VT codes.
- **InputCapture** *(INPUT_CAPTURE flags enum)* - Controls what specialized keys should be captured by the control (tab, arrow keys).
- **FontSizeWhenSettingTheme** *(int, 12)* - The font size for terminal text, if you set this after the control is initialized it won't take effect until you call `SetTheme` directly.
- **FontFamilyWhenSettingTheme** *(FontFamily, "Cascadia Code")* - Font to use for terminal text, similar behavior to font-size above in terms of changes after initialized
- **ConPTYTerm** - Allows you to change the TermPTY
- **Terminal** *(write only)*

### Methods
#### EasyTerminalControl
- `DisconnectConPTYTerm()` - Mostly useful if you plan to connect another ConPTYTerm to the frontend

#### TermPTY
- `TermPTY(int READ_BUFFER_SIZE = 1024 * 16, bool USE_BINARY_WRITER = false, IProcessFactory ProcessFactory=null)` - The read buffer is the maximum that can be read at once, rarely would it need to be changed.  By default data is transmitted as UTF8 text however conpty does support binary reading and writing.  No matter what mode you use, you can use the `WriteToTerm` or `WriteToTermBinary` functions and it will automatically transform them to bytes/text automatically.  Of course if you are working with binary data but don't have binary writing on you will run into problems when it attempts to call Encoding.UTF8.GetString on those bytes.  IProcessFactory interface is responsible for creating the actual process.  If you need more control you can implement this but please review the default implementation first.  There are some [specific things](https://docs.microsoft.com/en-us/windows/console/creating-a-pseudoconsole-session#preparing-for-creation-of-the-child-process) that must be set or else the console interaction may not work properly.
- `ClearUITerminal(bool fullReset = false)` - Clears the screen using the proper VT sequences
- `WriteToUITerminal(ReadOnlySpan<char> str)` - This simulates output to the Terminal UI
- `WriteToTerm(ReadOnlySpan<char> input)` - Write to the conpty directly as if input came from the Terminal UI
- `WriteToTermBinary(ReadOnlySpan<byte> input)` - Write to the conpty directly taking a byte array.  Note if USE_BINARY_WRITER is not enabled this will just be converted to a UTF8 string.
- `InterceptOutputToUITerminal`/`InterceptInputToTermApp` both of these take a delegate: `void InterceptDelegate(ref Span<char> str)` they allow you to manipulate the data that will be sent.  You can return a whole new span if desired.

### Theming
The terminal can take a Theme object setting the default foreground/background/highlight colors and then the standard 16 vt100 colors.  The colors are (COLORREF)[https://learn.microsoft.com/en-us/windows/win32/gdi/colorref] values.  There is a helper EasyTerminalControl.ColorToVal to convert Color values to COLORREF format.  Note there is no transparency support currently with the control.  As the control has its own HWND there are methods to set the entire layers transparency (note that would affect the foreground and background text).

## Limitations
Sadly nothing is free. The way the Windows Terminal team is able to provide such great performance and functionality without the normal WPF limitations is by doing all their own render handling with native C++, DirectX, and their atlas engine.  This means the UI control itself uses Hwnd hosting (similar to xaml islands but for native controls).  Most of this work is completely hidden from you and it behaves like any normal WPF control (resizing, backend api call / property / function calls) except for airspace.  The primary limitation with airspace is in terms of trying render WPF content on top of the terminal itself.  Context menus and such will work but normal WPF controls cannot appear above the terminal part itself. If you have used the WebView2 control this the same sort of limitation (see their bug [#286](https://github.com/MicrosoftEdge/WebView2Feedback/issues/286)). For more details on airspace in general see the Microsoft documentation on [layered rendering](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/technology-regions-overview). There are some ways around the stock limitations (ie you can specify arbitrary shapes at runtime that punch through certain sections of some layers),  but that is beyond the scope of this project.

## Advanced Usage / Internals
Behind the scenes there are two main parts to the control.  There is the [Pseudo Console (conpty.dll)](https://devblogs.microsoft.com/commandline/windows-command-line-introducing-the-windows-pseudo-console-conpty/) this is what hooks directly to the executable on one side and our library on the other (the `TermPTY` class in particular).  Then there is the Terminal GUI control `TerminalControl` that handles the actual rendering (the massive work done by the Windows Terminal Team). The Terminal GUI not only renders output but also takes user input and converts it into the proper VT codes for sending to the backend tty.  The interactions between these two is managed by our BasicTerminalControl.

As we use our own class `TermPTY` to control conpty it allows us to do interesting things.  Normally TermPTY is fairly passive input and output are passed transparently between the Terminal GUI and the backend conpty/application.  You can directly write ANSI/VT/text to either the Terminal GUI or the backend conpty.  For example, while normally input to the application is what the user types if you had an AI helper it could directly inject input into the application without having to simulate keystrokes.  Similarly your application could interpret program output and then change the ANSI sequences sent to the Terminal GUI to do things like provided colorized output for applications that don't support it.   NOTE:  There are strict limitations here, normally these two components are tightly integrated together.  If you change the output of complex programs to the Terminal GUI you may get strange results (think about sequences sent to change the cursor position and then that cursor not being where its expected).

### ReadDelimitedTermPTY
There is an additional ConPTY class called `ReadDelimitedTermPTY`.  This is a special ConPTY version it only shows the output from the program every time a specific delimiter found in the output (and only the output before the delimiter). It has some special logic to try and efficiently reuse the same buffer as a ring buffer so that even if the delimiter is only partially output in one write from the program it will catch it will still match when the rest is written on the next write.  There is an example of it in the TermExample app with the Process Output Sample button and window.
