using System;
using System.IO;

namespace CodeGenerator;

class CSharpCodeWriter(string outputPath) : IDisposable
{
    private const int IndentSize = 4;

    private readonly StreamWriter _sw = File.CreateText(outputPath);
    private int _indentLevel = 0;

    public void Using(string ns)
    {
        WriteIndented($"using {ns};");
    }

    public void PushBlock(string blockHeader)
    {
        WriteIndented(blockHeader);
        WriteIndented("{");
        _indentLevel += IndentSize;
    }

    public void PopBlock()
    {
        _indentLevel -= IndentSize;
        WriteIndented("}");
    }

    public void WriteLine(string text)
    {
        WriteIndented(text);
    }

    private void WriteIndented(string text)
    {
        if (_indentLevel > 0)
            _sw.Write(new string(' ', _indentLevel));
        _sw.WriteLine(text);
    }

    public void WriteRaw(string text)
    {
        _sw.WriteLine(text);
    }

    public void IndentManually()
    {
        _indentLevel += IndentSize;
    }

    public void Dispose()
    {
        _sw.Dispose();
    }
}
