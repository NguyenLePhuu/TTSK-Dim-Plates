[CmdletBinding(DefaultParameterSetName='Generate')]
param(
    [Parameter(ParameterSetName='Check')][switch]$Check,
    [Parameter(Mandatory,ParameterSetName='Find')][ValidateNotNullOrEmpty()][string]$Find,
    [Parameter(ParameterSetName='Find')][string]$File = '',
    [Parameter(ParameterSetName='Find')][ValidateSet('Any','Type','Method','Field','Property','Event','Namespace','Constant','Local','Alias','Attribute','Region')][string]$Kind = 'Any',
    [Parameter(ParameterSetName='Find')][ValidateRange(1,100)][int]$Limit = 12
)

# Bản đồ ngắn + tra khai báo theo yêu cầu. Cache nội bộ được kiểm hash mỗi lần;
# không chạy assembly sản phẩm/Tekla. Không tham số: cập nhật; -Check: xác minh.
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$workRoot = Join-Path $repoRoot '.codex-artifacts\code-map-tool'
New-Item -ItemType Directory -Path $workRoot -Force | Out-Null
$sdkLines = & dotnet --list-sdks
if ($LASTEXITCODE -ne 0) { throw 'Cần .NET SDK 8 trở lên cho công cụ tra cứu.' }
$majors = @($sdkLines | ForEach-Object { if ($_ -match '^(\d+)\.') { [int]$Matches[1] } } | Sort-Object -Descending)
if (!$majors.Count -or $majors[0] -lt 8) { throw 'Cần .NET SDK 8 trở lên; runtime sản phẩm vẫn là net48.' }
$sdkMajor = $majors[0]
$toolProject = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net$sdkMajor.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><NuGetAudit>false</NuGetAudit></PropertyGroup>
  <ItemGroup>
    <Reference Include="Microsoft.CodeAnalysis"><HintPath>`$(MSBuildSDKsPath)/../Roslyn/bincore/Microsoft.CodeAnalysis.dll</HintPath></Reference>
    <Reference Include="Microsoft.CodeAnalysis.CSharp"><HintPath>`$(MSBuildSDKsPath)/../Roslyn/bincore/Microsoft.CodeAnalysis.CSharp.dll</HintPath></Reference>
  </ItemGroup>
</Project>
"@
$toolSource = @'
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

static class CodeMap
{
    const string Marker = "<!-- BEGIN GENERATED CODE MAP -->";
    static string Root, Source;
    static string Relative(string path) => Path.GetRelativePath(Root,path).Replace('\\','/');
    static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

    static string One(string value) => Regex.Replace(value ?? "", @"\s+", " ").Trim();
    static string Short(string value, int max=180) {value=One(value);return value.Length<=max?value:value[..max]+" …";}
    static string Span(SyntaxNode node) {var s=node.GetLocation().GetLineSpan();return "L"+(s.StartLinePosition.Line+1)+"-L"+(s.EndLinePosition.Line+1);}
    static string Name(BaseTypeDeclarationSyntax t) => t.Identifier.Text+(t is TypeDeclarationSyntax d?d.TypeParameterList?.ToString():"");
    static string Owner(SyntaxNode n) => string.Join(".",n.Ancestors().Reverse().Select(a=>a is BaseNamespaceDeclarationSyntax ns?ns.Name.ToString():a is BaseTypeDeclarationSyntax t?Name(t):null).Where(s=>s!=null));
    static string Mods(SyntaxTokenList mods) => string.Join(" ",mods.Select(t=>t.Text));
    static string Signature(MemberDeclarationSyntax m) => m switch
    {
        BaseNamespaceDeclarationSyntax n => "NS "+n.Name,
        TypeDeclarationSyntax t => "TYPE "+Mods(t.Modifiers)+" "+t.Keyword.Text+" "+Name(t)+" "+t.BaseList+" "+string.Join(" ",t.ConstraintClauses),
        EnumDeclarationSyntax t => "TYPE "+Mods(t.Modifiers)+" enum "+Name(t)+" "+t.BaseList,
        DelegateDeclarationSyntax d => "TYPE delegate "+d.ReturnType+" "+d.Identifier+d.TypeParameterList+d.ParameterList,
        MethodDeclarationSyntax d => "M "+Mods(d.Modifiers)+" "+d.ReturnType+" "+d.ExplicitInterfaceSpecifier+d.Identifier+d.TypeParameterList+d.ParameterList+" "+string.Join(" ",d.ConstraintClauses),
        ConstructorDeclarationSyntax d => "CTOR "+Mods(d.Modifiers)+" "+d.Identifier+d.ParameterList,
        DestructorDeclarationSyntax d => "DTOR ~"+d.Identifier+d.ParameterList,
        FieldDeclarationSyntax d => "F "+Mods(d.Modifiers)+" "+d.Declaration.Type+" "+string.Join("; ",d.Declaration.Variables.Select(v=>v.Identifier.Text+(v.Initializer!=null?" = "+Short(v.Initializer.Value.ToString()):""))),
        EventFieldDeclarationSyntax d => "E "+Mods(d.Modifiers)+" "+d.Declaration.Type+" "+string.Join("; ",d.Declaration.Variables.Select(v=>v.Identifier.Text)),
        PropertyDeclarationSyntax d => "P "+Mods(d.Modifiers)+" "+d.Type+" "+d.ExplicitInterfaceSpecifier+d.Identifier+" "+Accessors(d.AccessorList)+(d.Initializer!=null?" = "+Short(d.Initializer.Value.ToString()):""),
        IndexerDeclarationSyntax d => "P "+Mods(d.Modifiers)+" "+d.Type+" this"+d.ParameterList+" "+Accessors(d.AccessorList),
        EventDeclarationSyntax d => "E "+Mods(d.Modifiers)+" "+d.Type+" "+d.Identifier+" "+Accessors(d.AccessorList),
        EnumMemberDeclarationSyntax d => "ENUM "+d.Identifier+(d.EqualsValue!=null?" = "+Short(d.EqualsValue.Value.ToString()):""),
        OperatorDeclarationSyntax d => "OP "+d.ReturnType+" operator "+d.OperatorToken+d.ParameterList,
        ConversionOperatorDeclarationSyntax d => "OP "+d.ImplicitOrExplicitKeyword+" operator "+d.Type+d.ParameterList,
        _ => throw new Exception("Unsupported declaration kind: "+m.Kind()+" "+Span(m))
    };
    static string Accessors(AccessorListSyntax list) => list==null?"=> biểu thức":"{ "+string.Join(" ",list.Accessors.Select(a=>a.Keyword.Text+"@"+Span(a)))+" }";
    static bool Exclude(string path) => Relative(path).Split('/').Any(x=>new[]{"bin","obj","packages",".vs","backup","backups","node_modules"}.Contains(x,StringComparer.OrdinalIgnoreCase));
    record Entry(string File, int Line, int End, string Owner, string Name, string Kind, string Signature);
    record Catalogue(string Fingerprint, int CompileCount, int MacroCount, int Declarations, int Types, int Methods, List<Entry> Entries);
    static string Kind(MemberDeclarationSyntax m) => m switch {
        BaseNamespaceDeclarationSyntax => "Namespace",
        BaseTypeDeclarationSyntax or DelegateDeclarationSyntax => "Type",
        BaseMethodDeclarationSyntax => "Method",
        FieldDeclarationSyntax => "Field", EventFieldDeclarationSyntax or EventDeclarationSyntax => "Event",
        PropertyDeclarationSyntax or IndexerDeclarationSyntax => "Property", EnumMemberDeclarationSyntax => "Constant",
        _ => throw new Exception("Unsupported declaration: "+m.Kind())
    };
    static string Identifier(MemberDeclarationSyntax m) => m switch {
        BaseNamespaceDeclarationSyntax n=>n.Name.ToString(), BaseTypeDeclarationSyntax t=>Name(t),
        DelegateDeclarationSyntax d=>d.Identifier.Text, MethodDeclarationSyntax d=>d.Identifier.Text,
        ConstructorDeclarationSyntax d=>d.Identifier.Text, DestructorDeclarationSyntax d=>"~"+d.Identifier.Text,
        BaseFieldDeclarationSyntax d=>string.Join(",",d.Declaration.Variables.Select(v=>v.Identifier.Text)),
        PropertyDeclarationSyntax d=>d.Identifier.Text, IndexerDeclarationSyntax=>"this",
        EventDeclarationSyntax d=>d.Identifier.Text, EnumMemberDeclarationSyntax d=>d.Identifier.Text,
        OperatorDeclarationSyntax d=>"operator "+d.OperatorToken, ConversionOperatorDeclarationSyntax d=>"operator "+d.Type,
        _=>throw new Exception("Unsupported declaration: "+m.Kind())
    };
    static Entry MakeEntry(string file,SyntaxNode node,string name,string kind,string signature) {
        var p=node.GetLocation().GetLineSpan();return new(file,p.StartLinePosition.Line+1,p.EndLinePosition.Line+1,Owner(node),name,kind,One(signature));
    }
    static bool Matches(string text,string pattern) {
        if(string.IsNullOrWhiteSpace(pattern))return true;
        if(!pattern.Contains('*')&&!pattern.Contains('?'))return text.Contains(pattern,StringComparison.OrdinalIgnoreCase);
        return Regex.IsMatch(text,Regex.Escape(pattern).Replace(@"\*",".*").Replace(@"\?","."),RegexOptions.IgnoreCase|RegexOptions.CultureInvariant,TimeSpan.FromSeconds(1));
    }
    static int Main(string[] args) {
        try {return Run(args);} catch(Exception ex){Console.Error.WriteLine("ERROR: "+ex.Message);return 1;}
    }
    static int Run(string[] args) {
        Root=Path.GetFullPath(args[0]);Source=Path.Combine(Root,"TTSK Dim Plates","TTSK Dim Plates");
        string mode=args[1],mapPath=Path.Combine(Root,"PROJECT_MAP.md");
        string old=File.ReadAllText(mapPath).Replace("\r\n","\n");int mark=old.IndexOf(Marker,StringComparison.Ordinal);
        if(mark<0||old.LastIndexOf(Marker,StringComparison.Ordinal)!=mark)throw new Exception("PROJECT_MAP.md requires one generated marker.");
        string manual=old[..mark];string project=Path.Combine(Source,"TTSK Dim Plates.csproj");
        var xml=XDocument.Load(project);XNamespace ns=xml.Root.Name.Namespace;
        var compiles=xml.Descendants(ns+"Compile").Select(e=>e.Attribute("Include")?.Value.Replace('\\','/')).ToArray();
        if(compiles.Any(string.IsNullOrEmpty)||compiles.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=compiles.Length)throw new Exception("Missing/duplicate Compile items.");
        var contents=xml.Descendants(ns+"Content").Select(e=>e.Attribute("Include")?.Value.Replace('\\','/')).Where(p=>p!=null).ToArray();
        var macros=contents.Where(p=>p.EndsWith(".cs",StringComparison.OrdinalIgnoreCase)).ToArray();
        var sources=compiles.Concat(macros).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(p=>p,StringComparer.Ordinal).ToArray();
        foreach(string f in sources)if(!File.Exists(Path.Combine(Source,f)))throw new Exception("Missing source: "+f);
        var orphan=Directory.EnumerateFiles(Source,"*.cs",SearchOption.AllDirectories).Where(p=>!Exclude(p)).Select(p=>Path.GetRelativePath(Source,p).Replace('\\','/')).Except(sources,StringComparer.OrdinalIgnoreCase).ToArray();
        if(orphan.Length>0)throw new Exception("C# outside project: "+string.Join(", ",orphan));
        // Chỉ mục là dữ liệu cục bộ, không nạp vào ngữ cảnh AI. Hash trước mỗi truy vấn để tránh trả số dòng cũ.
        var inputs=new SortedSet<string>(StringComparer.Ordinal){project,Path.Combine(Root,"AGENTS.md"),Path.Combine(Root,"README.md")};
        foreach(string f in sources)inputs.Add(Path.Combine(Source,f));
        foreach(var item in xml.Descendants().Where(e=>new[]{"Content","None","EmbeddedResource"}.Contains(e.Name.LocalName)&&e.Attribute("Include")!=null)) {
            string p=Path.Combine(Source,item.Attribute("Include").Value);if(File.Exists(p))inputs.Add(p);
        }
        foreach(string folder in new[]{"scripts","tests","docs","update",".github"}) {
            string dir=Path.Combine(Root,folder);if(!Directory.Exists(dir))continue;
            foreach(string p in Directory.EnumerateFiles(dir,"*",SearchOption.AllDirectories).Where(p=>!Exclude(p)&&new[]{".cs",".csproj",".ps1",".bat",".md",".yml",".yaml",".json",".txt",".tsv"}.Contains(Path.GetExtension(p).ToLowerInvariant())))inputs.Add(p);
        }
        foreach(string p in Directory.EnumerateFiles(Path.Combine(Root,"TTSK Dim Plates"),"*.sln*",SearchOption.TopDirectoryOnly))inputs.Add(p);
        foreach(string p in Directory.EnumerateFiles(Root,"*",SearchOption.TopDirectoryOnly).Where(p=>new[]{".gitignore",".gitattributes","global.json","NuGet.Config","nuget.config","Directory.Build.props","Directory.Build.targets"}.Contains(Path.GetFileName(p))||p.EndsWith(".bat",StringComparison.OrdinalIgnoreCase)))inputs.Add(p);
        string fingerprint=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("map-schema-2\n"+string.Join("\n",inputs.OrderBy(Relative,StringComparer.Ordinal).Select(p=>Relative(p)+"="+Hash(p)))))).ToLowerInvariant();
        string cache=Path.Combine(Root,".codex-artifacts","code-map-tool","catalogue-v2.json");Catalogue catalogue=null;
        if(File.Exists(cache)) {try {catalogue=JsonSerializer.Deserialize<Catalogue>(File.ReadAllText(cache));}catch(JsonException){}}
        if(catalogue==null||catalogue.Fingerprint!=fingerprint) {
            var entries=new List<Entry>();int declarations=0,types=0,methods=0;
            foreach(string file in sources) {
                string path=Path.Combine(Source,file),text=File.ReadAllText(path);
                var trees=new[]{new[]{"TRACE"},new[]{"DEBUG","TRACE"}}.Select(symbols=>CSharpSyntaxTree.ParseText(text,new CSharpParseOptions(LanguageVersion.CSharp7_3,preprocessorSymbols:symbols),path)).ToArray();
                foreach(var tree in trees)if(tree.GetDiagnostics().Any(d=>d.Severity==DiagnosticSeverity.Error))throw new Exception("Syntax error: "+file);
                var units=trees.Select(t=>t.GetCompilationUnitRoot()).ToArray();
                var members=units.SelectMany(u=>u.DescendantNodes().OfType<MemberDeclarationSyntax>()).GroupBy(n=>(n.RawKind,n.SpanStart,n.Span.Length)).Select(g=>g.First()).OrderBy(n=>n.SpanStart).ToArray();
                int emitted=0;
                foreach(var m in members) {entries.Add(MakeEntry(file,m,Identifier(m),Kind(m),Signature(m)));emitted++;if(m is BaseTypeDeclarationSyntax or DelegateDeclarationSyntax)types++;if(m is BaseMethodDeclarationSyntax)methods++;}
                if(emitted!=members.Length)throw new Exception("Coverage mismatch: "+file);declarations+=emitted;
                foreach(var local in units.SelectMany(u=>u.DescendantNodes().OfType<LocalFunctionStatementSyntax>()).GroupBy(n=>n.SpanStart).Select(g=>g.First()))entries.Add(MakeEntry(file,local,local.Identifier.Text,"Local",local.ReturnType+" "+local.Identifier+local.TypeParameterList+local.ParameterList));
                foreach(var c in units.SelectMany(u=>u.DescendantNodes().OfType<LocalDeclarationStatementSyntax>()).Where(c=>c.Modifiers.Any(SyntaxKind.ConstKeyword)).GroupBy(n=>n.SpanStart).Select(g=>g.First()))entries.Add(MakeEntry(file,c,string.Join(",",c.Declaration.Variables.Select(v=>v.Identifier.Text)),"Constant",Short(c.Declaration.ToString(),300)));
                foreach(var alias in units.SelectMany(u=>u.DescendantNodes().OfType<UsingDirectiveSyntax>()).Where(u=>u.Alias!=null).GroupBy(n=>n.SpanStart).Select(g=>g.First()))entries.Add(MakeEntry(file,alias,alias.Alias.Name.ToString(),"Alias",alias.ToString()));
                foreach(var attr in units.SelectMany(u=>u.AttributeLists).GroupBy(n=>n.SpanStart).Select(g=>g.First()))entries.Add(MakeEntry(file,attr,attr.ToString(),"Attribute",Short(attr.ToString(),300)));
                foreach(var region in units.SelectMany(u=>u.DescendantTrivia(descendIntoTrivia:true)).Where(t=>t.IsKind(SyntaxKind.RegionDirectiveTrivia)).GroupBy(t=>t.SpanStart).Select(g=>g.First())) {
                    var node=region.GetStructure();entries.Add(MakeEntry(file,node,One(region.ToString().Replace("#region","")),"Region",region.ToString()));
                }
            }
            catalogue=new(fingerprint,compiles.Length,macros.Length,declarations,types,methods,entries);
            Directory.CreateDirectory(Path.GetDirectoryName(cache));string tmp=cache+".tmp";
            File.WriteAllText(tmp,JsonSerializer.Serialize(catalogue),new UTF8Encoding(false));File.Move(tmp,cache,true);
        }
        if(mode=="find") {
            string term=args[2],file=args[3].Replace('\\','/'),kind=args[4];int limit=int.Parse(args[5]);
            var found=catalogue.Entries.Where(e=>Matches(e.File,file)&&(kind=="Any"||e.Kind.Equals(kind,StringComparison.OrdinalIgnoreCase))&&Matches(e.Owner+"."+e.Name,term))
                .OrderByDescending(e=>e.Name.Equals(term,StringComparison.OrdinalIgnoreCase)).ThenBy(e=>e.File,StringComparer.Ordinal).ThenBy(e=>e.Line).ToArray();
            Console.WriteLine($"Kết quả: {found.Length}; hiển thị {Math.Min(limit,found.Length)}. Đường dẫn tính từ TTSK Dim Plates/TTSK Dim Plates/.");
            foreach(var e in found.Take(limit))Console.WriteLine($"{e.File}:{e.Line}-{e.End} | {e.Kind} | {e.Owner} | {Short(e.Signature,200)}");
            if(found.Length>limit)Console.WriteLine("Còn kết quả: thu hẹp -File/-Kind/-Find hoặc tăng -Limit (tối đa 100).");
            if(found.Length==0)Console.WriteLine("Không có khai báo khớp. Dùng rg -n trong source để tìm nơi gọi hoặc chuỗi reflection.");
            return 0;
        }
        // Bản đồ chỉ giữ các nhóm chức năng và xác nhận phủ file; chi tiết khai báo trả theo truy vấn.
        var uncovered=sources.Where(f=>!manual.Contains("`"+f+"`",StringComparison.Ordinal)).ToArray();
        if(uncovered.Length>0)throw new Exception("Add missing file routes to PROJECT_MAP.md: "+string.Join(", ",uncovered));
        var b=new StringBuilder();void Line(string s="")=>b.Append(s).Append('\n');
        Line("## Độ phủ kiểm bằng máy");Line();
        Line($"- Bảng điều hướng bao phủ **{sources.Length}/{sources.Length} file C#**: {compiles.Length} Compile + {macros.Length} macro Content; không có C# ngoài danh mục project.");
        Line($"- Tra theo yêu cầu: **{catalogue.Declarations} khai báo**, **{catalogue.Types} kiểu/delegate**, **{catalogue.Methods} method/constructor/operator**; kể cả partial/nested type, field, property, event, enum và hằng cục bộ.");
        Line("- Phân tích C# 7.3, hợp TRACE/DEBUG+TRACE. Độ phủ cấu trúc không chứng minh mọi hành vi runtime; caller/chuỗi reflection tìm trong source.");
        Line("- Chỉ mục cục bộ tự tạo lại khi đầu vào đổi hoặc sang máy mới; không đọc trực tiếp file cache.");
        Line("<!-- source-fingerprint: "+fingerprint+" -->");
        string result=manual.TrimEnd()+"\n\n"+Marker+"\n\n"+b;
        if(mode=="check") {
            if(result!=old){Console.Error.WriteLine("STALE: PROJECT_MAP.md chưa khớp đầu vào hiện tại; chạy script không tham số để cập nhật.");return 1;}
            Console.WriteLine($"PASS: {sources.Length}/{sources.Length} file có đường dẫn tra; {catalogue.Declarations} khai báo tra được; bản đồ khớp source.");return 0;
        }
        if(mode!="generate")throw new Exception("Unknown mode: "+mode);
        File.WriteAllText(mapPath,result,new UTF8Encoding(false));Console.WriteLine($"Đã cập nhật PROJECT_MAP.md: {result.Count(c=>c=='\n')} dòng; {sources.Length}/{sources.Length} file; {catalogue.Declarations} khai báo tra theo yêu cầu.");return 0;
    }
}

'@
function Write-ToolInput([string]$path,[string]$text) {
    if (!(Test-Path -LiteralPath $path) -or [IO.File]::ReadAllText($path) -cne $text) {
        [IO.File]::WriteAllText($path,$text,[Text.UTF8Encoding]::new($false))
        return $true
    }
    return $false
}
$projectChanged = Write-ToolInput (Join-Path $workRoot 'CodeMap.csproj') $toolProject
$sourceChanged = Write-ToolInput (Join-Path $workRoot 'Program.cs') $toolSource
$configChanged = Write-ToolInput (Join-Path $workRoot 'NuGet.Config') '<configuration><packageSources><clear /></packageSources></configuration>'
$toolDll = Join-Path $workRoot "bin\Release\net$sdkMajor.0\CodeMap.dll"
if ($projectChanged -or $sourceChanged -or $configChanged -or !(Test-Path -LiteralPath $toolDll)) {
    $buildLog = Join-Path $workRoot 'build.log'
    & dotnet build (Join-Path $workRoot 'CodeMap.csproj') -c Release --nologo -v:q *> $buildLog
    if ($LASTEXITCODE -ne 0) { Get-Content $buildLog -Tail 15; throw 'Build công cụ tra cứu thất bại.' }
}
if ($Check) { & dotnet $toolDll $repoRoot check }
elseif ($PSCmdlet.ParameterSetName -eq 'Find') { & dotnet $toolDll $repoRoot find $Find $File $Kind $Limit }
else { & dotnet $toolDll $repoRoot generate }
if ($LASTEXITCODE -ne 0) { throw 'Kiểm tra/tra cứu bản đồ thất bại.' }
