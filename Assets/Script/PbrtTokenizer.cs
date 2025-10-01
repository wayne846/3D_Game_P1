using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

public class PbrtTokenizer
{
    private readonly Queue<string> _tokens;

    public PbrtTokenizer(string fileContent)
    {
        string noComments = Regex.Replace(fileContent, @"#.*", "");
        string spacedBrackets = noComments.Replace("[", " [ ").Replace("]", " ] ");
        var matches = Regex.Matches(spacedBrackets, @"""[^""]*""|\S+");
        
        _tokens = new Queue<string>();
        foreach (Match match in matches)
        {
            // --- FIX: Do NOT trim quotes here. Preserve them for the parser. ---
            _tokens.Enqueue(match.Value);
        }
    }

    public bool HasMoreTokens() => _tokens.Count > 0;
    public string GetNextToken() => _tokens.Count > 0 ? _tokens.Dequeue() : null;
    public string PeekNextToken() => _tokens.Count > 0 ? _tokens.Peek() : null;
}