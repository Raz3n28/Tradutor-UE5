using System;
using System.Diagnostics;

// O que os arquivos vindos do ModLive usavam do Unity, feito em .NET puro
// (assim Cache, Glossario, Categorias e Motor ficam quase iguais aos do mod original).
internal static class Mathf
{
    public static int Clamp(int v, int min, int max) { return v < min ? min : (v > max ? max : v); }
    public static float Clamp(float v, float min, float max) { return v < min ? min : (v > max ? max : v); }
    public static int Max(int a, int b) { return a > b ? a : b; }
    public static float Max(float a, float b) { return a > b ? a : b; }
    public static int Min(int a, int b) { return a < b ? a : b; }
    public static float Min(float a, float b) { return a < b ? a : b; }
    public static int CeilToInt(float v) { return (int)Math.Ceiling(v); }
    public static int RoundToInt(float v) { return (int)Math.Round(v); }
}

internal static class Time
{
    private static readonly Stopwatch relogio = Stopwatch.StartNew();

    // segundos desde que o cerebro abriu (como o Time.unscaledTime do Unity)
    public static float unscaledTime { get { return (float)relogio.Elapsed.TotalSeconds; } }
}

internal struct Color
{
    public float r, g, b, a;

    public Color(float r, float g, float b, float a)
    {
        this.r = r; this.g = g; this.b = b; this.a = a;
    }

    public System.Drawing.Color ParaDrawing()
    {
        return System.Drawing.Color.FromArgb((int)(a * 255), (int)(r * 255), (int)(g * 255), (int)(b * 255));
    }
}
