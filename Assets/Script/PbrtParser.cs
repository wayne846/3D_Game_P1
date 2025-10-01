using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;

// 通用參數字典
using PbrtParams = System.Collections.Generic.Dictionary<string, object>;
public class PbrtParser
{
    private readonly PbrtScene _scene = new PbrtScene();
    private readonly Stack<Matrix4x4> _transformStack = new Stack<Matrix4x4>();
    // --- NEW: Stack to manage material state ---
    private readonly Stack<PbrtMaterial> _materialStack = new Stack<PbrtMaterial>();
    private string _baseDirectory;
    private PbrtLight _pendingLight = null;

    public PbrtScene Parse(string filePath)
    {
        _baseDirectory = Path.GetDirectoryName(filePath);
        _transformStack.Push(Matrix4x4.identity);
        // --- NEW: Initialize material stack with a null (default) material ---
        _materialStack.Push(null);

        Debug.Log("PBRT parsing started...");
        ParseFile(filePath);

        long finalMeshCount = _scene.Shapes.LongCount(s => s is PbrtTriangleMesh);
        Debug.Log($"PBRT parsing finished. Found {finalMeshCount} triangle meshes.");

        return _scene;
    }

    private void ParseFile(string filePath)
    {
        if (!File.Exists(filePath))
        {
            Debug.LogError($"File not found: {filePath}");
            return;
        }
        string content = File.ReadAllText(filePath);
        var tokenizer = new PbrtTokenizer(content);
        ParseTokens(tokenizer);
    }

    private void ParseTokens(PbrtTokenizer tokenizer)
    {
        while (tokenizer.HasMoreTokens())
        {
            string directive = tokenizer.PeekNextToken();
            if (directive == null) break;

            switch (directive)
            {
                // ... other cases are unchanged ...
                case "LookAt": tokenizer.GetNextToken(); ParseLookAt(tokenizer); break;
                case "Camera": tokenizer.GetNextToken(); ParseCamera(tokenizer); break;
                case "Sampler": tokenizer.GetNextToken(); ParseSampler(tokenizer); break;
                case "Film": tokenizer.GetNextToken(); ParseFilm(tokenizer); break;
                case "WorldBegin": case "WorldEnd": tokenizer.GetNextToken(); break;

                case "AttributeBegin":
                    tokenizer.GetNextToken();
                    _transformStack.Push(_transformStack.Peek());
                    // --- NEW: Push current material to save state ---
                    _materialStack.Push(_materialStack.Peek());
                    break;
                case "AttributeEnd":
                    tokenizer.GetNextToken();
                    _transformStack.Pop();
                    // --- NEW: Pop material to restore state ---
                    _materialStack.Pop();
                    _pendingLight = null;
                    break;

                case "Shape": tokenizer.GetNextToken(); ParseShape(tokenizer); break;
                case "AreaLightSource": tokenizer.GetNextToken(); _pendingLight = new PbrtLight { Type = tokenizer.GetNextToken().Trim('"'), Parameters = ParseParameters(tokenizer) }; break;

                // --- FIX: Correctly parse Material directives ---
                case "Material":
                    tokenizer.GetNextToken(); // Consume "Material"
                    string matType = tokenizer.GetNextToken().Trim('"');
                    PbrtParams matParams = ParseParameters(tokenizer);
                    var newMaterial = new PbrtMaterial { Type = matType, Parameters = matParams };
                    // Replace the current material on top of the stack
                    if (_materialStack.Count > 0) _materialStack.Pop();
                    _materialStack.Push(newMaterial);
                    break;

                case "Include": tokenizer.GetNextToken(); string includePath = Path.Combine(_baseDirectory, tokenizer.GetNextToken().Trim('"')); ParseFile(includePath); break;
                case "Translate": tokenizer.GetNextToken(); ParseTranslate(tokenizer); break;
                case "Rotate": tokenizer.GetNextToken(); ParseRotate(tokenizer); break;
                case "Scale": tokenizer.GetNextToken(); ParseScale(tokenizer); break;
                case "Accelerator": case "PixelFilter": case "Integrator": tokenizer.GetNextToken(); tokenizer.GetNextToken(); ParseParameters(tokenizer); break;
                default: tokenizer.GetNextToken(); break;
            }
        }
    }

    private void ParseShape(PbrtTokenizer tokenizer)
    {
        string type = tokenizer.GetNextToken().Trim('"');
        PbrtParams parameters = ParseParameters(tokenizer);
        PbrtShape shape = null;

        switch (type)
        {
            case "sphere": shape = new PbrtSphere(); break;
            case "cylinder": shape = new PbrtCylinder(); break;
            case "trianglemesh":
                var mesh = new PbrtTriangleMesh();
                if (parameters.TryGetValue("indices", out object indicesObj)) mesh.Indices = (int[])indicesObj;
                if (parameters.TryGetValue("P", out object pObj)) mesh.Vertices = (Vector3[])pObj;
                if (parameters.TryGetValue("N", out object nObj)) mesh.Normals = (Vector3[])nObj;
                if (mesh.Vertices != null && mesh.Indices != null) shape = mesh;
                break;
        }

        if (shape != null)
        {
            shape.Parameters = parameters;
            shape.ObjectToWorld = _transformStack.Peek();
            shape.AttachedLight = _pendingLight;
            // --- NEW: Assign the current material from the top of the stack ---
            shape.Material = _materialStack.Peek();
            _pendingLight = null;
            _scene.Shapes.Add(shape);
        }
    }

    private PbrtParams ParseParameters(PbrtTokenizer tokenizer)
    {
        var parameters = new PbrtParams();
        // FIX: The loop condition now works because quotes are preserved.
        while (tokenizer.HasMoreTokens() && tokenizer.PeekNextToken().StartsWith("\""))
        {
            string paramDesc = tokenizer.GetNextToken().Trim('"');
            string[] parts = paramDesc.Split(' ');
            string paramType = parts[0];
            string paramName = parts.Length > 1 ? parts[1] : "";

            tokenizer.GetNextToken(); // Consume "["
            object value = null;

            if (paramType == "integer" && paramName == "indices")
            {
                var ints = new List<int>();
                while (tokenizer.PeekNextToken() != "]") ints.Add(ReadInt(tokenizer));
                value = ints.ToArray();
            }
            else if ((paramType == "point" && paramName == "P") || (paramType == "normal" && paramName == "N"))
            {
                var vectors = new List<Vector3>();
                while (tokenizer.PeekNextToken() != "]") vectors.Add(new Vector3(ReadFloat(tokenizer), ReadFloat(tokenizer), ReadFloat(tokenizer)));
                value = vectors.ToArray();
            }
            else
            {
                switch (paramType)
                {
                    case "color":
                    case "point":
                    case "vector":
                        value = new Vector3(ReadFloat(tokenizer), ReadFloat(tokenizer), ReadFloat(tokenizer));
                        break;
                    case "integer": value = ReadInt(tokenizer); break;
                    case "float": value = ReadFloat(tokenizer); break;
                    case "string":
                        // CHANGE: Trim quotes from string parameter value
                        value = tokenizer.GetNextToken().Trim('"');
                        break;
                    default:
                        while (tokenizer.HasMoreTokens() && tokenizer.PeekNextToken() != "]") tokenizer.GetNextToken();
                        break;
                }
            }

            if (tokenizer.HasMoreTokens() && tokenizer.PeekNextToken() == "]") tokenizer.GetNextToken();

            if (value != null && !string.IsNullOrEmpty(paramName)) parameters[paramName] = value;
        }
        return parameters;
    }

    private float ReadFloat(PbrtTokenizer t) => float.Parse(t.GetNextToken(), CultureInfo.InvariantCulture);
    private int ReadInt(PbrtTokenizer t) => int.Parse(t.GetNextToken(), CultureInfo.InvariantCulture);

    private void ParseCamera(PbrtTokenizer t) { _scene.Camera = new PbrtCamera { Type = t.GetNextToken().Trim('"'), Parameters = ParseParameters(t) }; }
    private void ParseFilm(PbrtTokenizer t) { _scene.Film = new PbrtFilm { Type = t.GetNextToken().Trim('"'), Parameters = ParseParameters(t) }; }
    private void ParseSampler(PbrtTokenizer t) { _scene.Sampler = new PbrtSampler { Type = t.GetNextToken().Trim('"'), Parameters = ParseParameters(t) }; }

    private void ParseLookAt(PbrtTokenizer t)
    {
        Vector3 eye = new Vector3(ReadFloat(t), ReadFloat(t), ReadFloat(t));
        Vector3 lookAt = new Vector3(ReadFloat(t), ReadFloat(t), ReadFloat(t));
        Vector3 up = new Vector3(ReadFloat(t), ReadFloat(t), ReadFloat(t));
        if (_scene.Camera == null) _scene.Camera = new PbrtCamera();
        _scene.Camera.WorldToCameraMatrix = Matrix4x4.LookAt(eye, lookAt, up);
    }

    private void ApplyTransform(Matrix4x4 t)
    {
        Matrix4x4 current = _transformStack.Pop();
        _transformStack.Push(current * t);
    }

    private void ParseScale(PbrtTokenizer t) { ApplyTransform(Matrix4x4.Scale(new Vector3(ReadFloat(t), ReadFloat(t), ReadFloat(t)))); }
    private void ParseTranslate(PbrtTokenizer t) { ApplyTransform(Matrix4x4.Translate(new Vector3(ReadFloat(t), ReadFloat(t), ReadFloat(t)))); }
    private void ParseRotate(PbrtTokenizer t)
    {
        float angle = ReadFloat(t);
        Vector3 axis = new Vector3(ReadFloat(t), ReadFloat(t), ReadFloat(t));
        ApplyTransform(Matrix4x4.Rotate(Quaternion.AngleAxis(angle, axis)));
    }
}