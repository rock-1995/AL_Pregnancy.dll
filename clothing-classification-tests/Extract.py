from pathlib import Path
import hashlib
import re
import sys

source = Path(sys.argv[1])
belly = (source / 'BellyVertexMorph.cs').read_text(encoding='utf-8-sig')
body = (source / 'BodyMeshSelection.cs').read_text(encoding='utf-8-sig')
visibility = (source / 'VisibilityMorph.cs').read_text(encoding='utf-8-sig')

def method(text, signature):
    start = text.index(signature)
    opening = text.index('{', start)
    depth = 0
    for index in range(opening, len(text)):
        if text[index] == '{': depth += 1
        elif text[index] == '}':
            depth -= 1
            if depth == 0: return text[start:index + 1]
    raise ValueError('Unclosed method: ' + signature)

members = [re.search(r'private static readonly string\[\] FaceKeywords\s*=.*?;', belly, re.S).group()]
for signature in ['private static bool IsBodyMorphClothSMR(', 'private static bool IsFaceMesh(', 'private static bool IsAccessoryAnchorPath(', 'private static string PathOf(']:
    members.append(method(belly, signature))
members.append(re.search(r'internal static bool IsClothingRenderer\(.*?;', visibility, re.S).group())
body_members = [re.search(r'private static readonly HashSet<int> RegisteredBodyPieces\s*=.*?;', body).group()]
for signature in ['internal static bool IsBodyPiece(', 'internal static void Register(']:
    body_members.append(method(body, signature))
header = '// Generated verbatim from production methods; do not edit.\n'
for name in ['BellyVertexMorph.cs', 'BodyMeshSelection.cs', 'VisibilityMorph.cs']:
    header += '// ' + name + ' SHA256 ' + hashlib.sha256((source / name).read_bytes()).hexdigest() + '\n'
output = header + 'using Character;\nusing UnityEngine;\nnamespace ALPregnancy;\ninternal static partial class BellyVertexMorph\n{\n' + '\n'.join(members) + '\n}\ninternal static class BodyMeshSelection\n{\n' + '\n'.join(body_members) + '\n}\n'
Path(sys.argv[2]).write_text(output, encoding='utf-8')
