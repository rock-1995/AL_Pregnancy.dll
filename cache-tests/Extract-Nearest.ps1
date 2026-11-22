$ErrorActionPreference = 'Stop'
$source = Join-Path $PSScriptRoot '../src/BellyVertexMorph.cs'
$text = [IO.File]::ReadAllText($source)
$signature = 'private static int[] BuildNearestBodyVertCache(Vector3[] overlayVerts, Vector3[] bodyVerts)'
$start = $text.IndexOf($signature, [StringComparison]::Ordinal)
if ($start -lt 0 -or $text.IndexOf($signature, $start + $signature.Length, [StringComparison]::Ordinal) -ge 0) {
    throw 'Production nearest-method signature must occur once.'
}
$open = $text.IndexOf('{', $start)
$depth = 0
$end = -1
for ($index = $open; $index -lt $text.Length; $index++) {
    if ($text[$index] -eq '{') { $depth++ }
    elseif ($text[$index] -eq '}') { $depth--; if ($depth -eq 0) { $end = $index + 1; break } }
}
if ($end -lt 0) { throw 'Unbalanced production source method.' }
$method = $text.Substring($start, $end - $start)
# Tests compare cached queries with a fresh direct search for the same input.
$direct = $method.Replace('BuildNearestBodyVertCache(', 'BuildNearestBodyVertCacheBaseline(')
$generated = "// Generated from the current production method; do not edit.`nusing UnityEngine;`nnamespace ALPregnancy;`ninternal static partial class BellyVertexMorph`n{`n$method`n$direct`n}`n"
[IO.File]::WriteAllText((Join-Path $PSScriptRoot 'ExtractedNearest.g.cs'), $generated)
