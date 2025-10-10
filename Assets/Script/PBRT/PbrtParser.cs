using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Assertions;


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
        // 設定預設的矩陣，將 xyz 轉成 zxy
        Matrix4x4 pbrt2unity = new Matrix4x4(new(0,0,1,0), new(1,0,0,0), new(0,1,0,0), new(0,0,0,1));
        _transformStack.Push(pbrt2unity);
        // --- NEW: Initialize material stack with a null (default) material ---
        _materialStack.Push(null);

        Debug.Log("PBRT parsing started...");
        ParseFile(filePath);

        long finalMeshCount = _scene.shapes.LongCount(s => s is PbrtTriangleMesh);
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
                case "AreaLightSource": tokenizer.GetNextToken(); _pendingLight = new PbrtLight { type = tokenizer.GetNextToken().Trim('"'), parameters = ParseParameters(tokenizer) }; break;

                // --- FIX: Correctly parse Material directives ---
                case "Material":tokenizer.GetNextToken(); ParseMaterial(tokenizer); break;
                case "Texture": tokenizer.GetNextToken(); ParseTexture(tokenizer); break;

                case "Include": tokenizer.GetNextToken(); string includePath = Path.Combine(_baseDirectory, tokenizer.GetNextToken().Trim('"')); ParseFile(includePath); break;

                // 矩陣
                case "Translate": tokenizer.GetNextToken(); ParseTranslate(tokenizer); break;
                case "Rotate": tokenizer.GetNextToken(); ParseRotate(tokenizer); break;
                case "Scale": tokenizer.GetNextToken(); ParseScale(tokenizer); break;
                case "ConcatTransform": tokenizer.GetNextToken(); ParseConcatTransform(tokenizer); break;

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
                if (parameters.TryGetValue("indices", out object indicesObj)) mesh.indices = (int[])indicesObj;
                if (parameters.TryGetValue("P", out object pObj)) mesh.vertices = (Vector3[])pObj;
                if (parameters.TryGetValue("N", out object nObj)) mesh.normals = (Vector3[])nObj;
                if (parameters.TryGetValue("st", out object st)) mesh.uvs = (Vector2[])st;
                if (mesh.vertices != null && mesh.indices != null) shape = mesh;
                break;
        }

        if (shape != null)
        {
            shape.parameters = parameters;
            shape.objectToWorld = _transformStack.Peek();
            shape.attachedLight = _pendingLight;
            // --- NEW: Assign the current material from the top of the stack ---
            shape.material = _materialStack.Peek();
            _pendingLight = null;
            _scene.shapes.Add(shape);
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
            Assert.IsTrue(parts.Length == 2, "Parameter must have type & name, but got    " + paramDesc);
            string paramType = parts[0];
            string paramName = parts[1];
            object value = null;

            bool WithLeftBracket = false;
            if (tokenizer.PeekNextToken() == "[")
            {
                WithLeftBracket = true;
                tokenizer.GetNextToken();
            }

            if (paramType == "integer" && paramName == "indices")
            {
                Assert.IsTrue(WithLeftBracket);
                var ints = new List<int>();
                while (tokenizer.PeekNextToken() != "]") ints.Add(ReadInt(tokenizer));
                value = ints.ToArray();
            }
            else if (((paramType == "point" || paramType == "point3") && paramName == "P") || (paramType == "normal" && paramName == "N"))
            {
                Assert.IsTrue(WithLeftBracket);
                var vectors = new List<Vector3>();
                while (tokenizer.PeekNextToken() != "]") vectors.Add(new Vector3(ReadFloat(tokenizer), ReadFloat(tokenizer), ReadFloat(tokenizer)));
                value = vectors.ToArray();
            }
            else if ((paramType == "point2" || paramType == "float") && paramName == "st")
            {
                Assert.IsTrue(WithLeftBracket);
                var vectors = new List<Vector2>();
                while (tokenizer.PeekNextToken() != "]") vectors.Add(new Vector2(ReadFloat(tokenizer), ReadFloat(tokenizer)));
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
                    case "texture":
                        string tex_name = tokenizer.GetNextToken().Trim('"');
                        value = _scene.KnownTexture[tex_name];
                        break;
                    default:
                        Assert.IsTrue(false, "Unknown Parameter Type: " + paramType);
                        break;
                }
            }

            if (WithLeftBracket)
            {
                Assert.IsTrue(tokenizer.PeekNextToken() == "]", "Unmatched [ for    " + paramDesc);
                tokenizer.GetNextToken();
            }

            Assert.IsTrue(value != null);
            parameters[paramName] = value;
        }
        return parameters;
    }

    private float ReadFloat(PbrtTokenizer t) => float.Parse(t.GetNextToken(), CultureInfo.InvariantCulture);
    private int ReadInt(PbrtTokenizer t) => int.Parse(t.GetNextToken(), CultureInfo.InvariantCulture);

    private void ParseCamera(PbrtTokenizer t)
    {
        // 1. 確保相機物件存在，如果不存在才建立
        if (_scene.camera == null)
        {
            _scene.camera = new PbrtCamera();
        }

        // 2. 在現有的相機物件上設定屬性，而不是建立新物件
        _scene.camera.type = t.GetNextToken().Trim('"');
        _scene.camera.parameters = ParseParameters(t);
    }
    private void ParseFilm(PbrtTokenizer t) { _scene.film = new PbrtFilm { type = t.GetNextToken().Trim('"'), parameters = ParseParameters(t) }; }
    private void ParseSampler(PbrtTokenizer t) { _scene.sampler = new PbrtSampler { type = t.GetNextToken().Trim('"'), parameters = ParseParameters(t) }; }

    private void ParseLookAt(PbrtTokenizer t)
    {
        Matrix4x4 M = _transformStack.Peek();
        
        Vector3 eye    = M                   * new Vector3(ReadFloat(t), ReadFloat(t), ReadFloat(t));
        Vector3 lookAt = M                   * new Vector3(ReadFloat(t), ReadFloat(t), ReadFloat(t));
        Vector3 up     = M.ExtractRotation() * new Vector3(ReadFloat(t), ReadFloat(t), ReadFloat(t));

        if (_scene.camera == null) _scene.camera = new PbrtCamera();
        _scene.camera.LookAt(eye, lookAt, up);
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

    private void ParseConcatTransform(PbrtTokenizer t)
    {
        Matrix4x4 M = new Matrix4x4();
        t.GetNextToken(); // consume [
        M.m00 = ReadFloat(t);
        M.m01 = ReadFloat(t);
        M.m02 = ReadFloat(t);
        M.m03 = ReadFloat(t);
        M.m10 = ReadFloat(t);
        M.m11 = ReadFloat(t);
        M.m12 = ReadFloat(t);
        M.m13 = ReadFloat(t);
        M.m20 = ReadFloat(t);
        M.m21 = ReadFloat(t);
        M.m22 = ReadFloat(t);
        M.m23 = ReadFloat(t);
        M.m30 = ReadFloat(t);
        M.m31 = ReadFloat(t);
        M.m32 = ReadFloat(t);
        M.m33 = ReadFloat(t);
        t.GetNextToken(); // consume ]

        ApplyTransform(M);
    }

    private void ParseMaterial(PbrtTokenizer t)
    {
        string matType = t.GetNextToken().Trim('"');
        PbrtParams matParams = ParseParameters(t);

        // Kd
        object Kd = new Vector3(1, 1, 1);
        if (matParams.TryGetValue("Kd", out object D))
        {
            Kd = D;
        }

        // Ks
        Vector3 Ks = new Vector3(0, 0, 0);
        if (matParams.TryGetValue("Ks", out object S))
        {
            Ks = (Vector3)S;
        }

        // Kt
        Vector3 Kt = new Vector3(0, 0, 0);
        if (matParams.TryGetValue("Kt", out object T))
        {
            Kt = (Vector3)T;
        }
        else if (matParams.TryGetValue("opacity", out object O))
        {
            Kt = (Vector3)O;
        }

        if (matType == "mirror")
        {
            Kd = new Vector3(0, 0, 0);
            Ks = new Vector3(1, 1, 1);
            Kt = new Vector3(0, 0, 0);
        }

        var newMaterial = new PbrtMaterial { type = matType, parameters = matParams, Kd = Kd, Ks = Ks, Kt = Kt };

        // Replace the current material on top of the stack
        if (_materialStack.Count > 0) _materialStack.Pop();
        _materialStack.Push(newMaterial);
    }

    private void ParseTexture(PbrtTokenizer t)
    {
        string tex_name = t.GetNextToken().Trim('"');
        t.GetNextToken(); // color / spectrum
        string tex_class = t.GetNextToken().Trim('"');
        PbrtParams tex_params = ParseParameters(t);

        Texture2D texture = new Texture2D(0, 0);

        switch (tex_class)
        {
            case "imagemap":
                string filename = (string)tex_params["filename"];
                byte[] data = File.ReadAllBytes(Path.Combine(_baseDirectory, filename));
                texture.LoadImage(data);
                break;

            case "scale":
                Texture2D from = (Texture2D)tex_params["tex1"];
                Vector3 scale = (Vector3)tex_params["tex2"];

                // 對原始貼圖每一像素乘上 scale
                texture.Reinitialize(from.width, from.height);
                for (int i = 0; i < from.width; i++)
                {
                    for (int j = 0; j < from.height; j++)
                    {
                        Color oldColor = from.GetPixel(i, j);
                        texture.SetPixel(i, j, new Color(oldColor.r * scale.x, oldColor.g * scale.y, oldColor.b * scale.z, oldColor.a));
                    }
                }
                texture.Apply();
                break;
        }

        Assert.IsTrue(texture.width * texture.height > 0);
        if (_scene.KnownTexture.ContainsKey(tex_name) == false)
            _scene.KnownTexture.Add(tex_name, texture);
    }
}