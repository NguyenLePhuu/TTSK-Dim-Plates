param([Parameter(Mandatory=$true)][string]$AssemblyPath)
$ErrorActionPreference = 'Stop'
foreach ($dll in @('Tekla.Structures','Tekla.Structures.Geometry3d.Compatibility','Tekla.Structures.Model','Tekla.Structures.Drawing')) {
    $null = [Reflection.Assembly]::LoadFrom("C:/Program Files/Tekla Structures/2025.0/bin/$dll.dll")
}
$assembly = [Reflection.Assembly]::LoadFrom((Resolve-Path $AssemblyPath).Path)
$engine = $assembly.GetType('Tekla.Technology.Akit.UserScript.PHU_Slot06DiagonalBraceDimensionEngine', $true)
$flags = [Reflection.BindingFlags]'Public,NonPublic,Static,Instance'
$pointType = $engine.GetNestedType('P2', $flags)
$planType = $engine.GetNestedType('DimPlan', $flags)
$tierType = $engine.GetNestedType('TierBand', $flags)
$listType = [System.Collections.Generic.List``1].MakeGenericType($planType)
$method = $engine.GetMethod('AddViewProjectedBoundaryPlan', $flags)
function Point($x, $y) { [Activator]::CreateInstance($pointType, $flags, $null, [object[]]@([double]$x,[double]$y), $null) }
function Field($obj, $name) { ,($obj.GetType().GetField($name, $flags).GetValue($obj)) }
$count = 0
# Captured left-chain feet: X spread exceeds Y despite a vertical technical role.
# Also exercise tall, mirrored, translated and reversed input chains.
foreach ($sx in @(1.0, -1.0, 0.1, 10.0)) {
    foreach ($reverse in @($false, $true)) {
        $coords = @(@(-3542.466,-900), @(-2987.430,-758.583), @(-2488.611,-149.071), @(-3073.466,0))
        if ($reverse) { [array]::Reverse($coords) }
        $points = [Array]::CreateInstance($pointType, 4)
        for ($i=0; $i -lt 4; $i++) { $points.SetValue((Point ($coords[$i][0]*$sx+123) ($coords[$i][1]-456)), $i) }
        foreach ($side in @('Top','Bottom','Left','Right')) {
            foreach ($level in @('Inner','Outer')) {
                $plans = [Activator]::CreateInstance($listType)
                $tier = [Enum]::Parse($tierType, "Boundary$side$level")
                $outside = Point $(if ($side -eq 'Left') {-1} else {1}) $(if ($side -eq 'Bottom') {-1} else {1})
                $null = $method.Invoke($null, [object[]]@($plans,$null,'test','boundary',$outside,$tier,$points))
                $plan = $plans[0]
                $axis = Field $plan 'MeasurementAxis'
                $normal = Field $plan 'PlacementNormal'
                $horizontal = [Math]::Abs((Field $points[3] 'X')-(Field $points[0] 'X')) -ge [Math]::Abs((Field $points[3] 'Y')-(Field $points[0] 'Y'))
                $ax = if ($horizontal) {1} else {0}
                $ay = 1-$ax
                $nx = if ($horizontal) {0} elseif ($side -eq 'Left') {-1} else {1}
                $ny = if (!$horizontal) {0} elseif ($side -eq 'Bottom') {-1} else {1}
                if ((Field $axis 'X') -ne $ax -or (Field $axis 'Y') -ne $ay -or (Field $normal 'X') -ne $nx -or (Field $normal 'Y') -ne $ny) { throw "Wrong axis/side: $side$level" }
                $feet = Field $plan 'Points'
                if ($feet.Count -ne 4) { throw 'Lost feet' }
                for ($i=0; $i -lt 4; $i++) {
                    if ((Field $feet[$i] 'X') -ne (Field $points[$i] 'X') -or (Field $feet[$i] 'Y') -ne (Field $points[$i] 'Y')) { throw 'Changed foot/order' }
                }
                $count++
            }
        }
    }
}
Write-Output "PASS: $count boundary cases; axes, sides and all original feet/order preserved."
# Fixed expected outcomes from the two real regressions, independent of tier names.
foreach ($case in @(
    @{Name='Type1 P-03'; Tier='BoundaryTopOuter'; Coords=@(@(4857.348,800),@(4857.348,2350)); Normal=@(1,0); Span=1550.0},
    @{Name='Type2 left'; Tier='BoundaryLeftInner'; Coords=@(@(-3542.466,-900),@(-2987.430,-758.583),@(-2488.611,-149.071),@(-3073.466,0)); Normal=@(-1,0); Span=900.0},
    @{Name='Type2 right'; Tier='BoundaryRightInner'; Coords=@(@(457.534,-900),@(-97.286,-758.584),@(-595.204,-149.071),@(-10.126,0)); Normal=@(1,0); Span=900.0}
)) {
    $points = [Array]::CreateInstance($pointType,$case.Coords.Count)
    for($i=0;$i -lt $points.Length;$i++) { $points.SetValue((Point $case.Coords[$i][0] $case.Coords[$i][1]),$i) }
    $plans = [Activator]::CreateInstance($listType)
    $null = $method.Invoke($null,[object[]]@($plans,$null,$case.Name,'regression',(Point $case.Normal[0] $case.Normal[1]),[Enum]::Parse($tierType,$case.Tier),$points))
    $axis = Field $plans[0] 'MeasurementAxis'
    $normal = Field $plans[0] 'PlacementNormal'
    if((Field $axis 'X') -ne 0 -or (Field $axis 'Y') -ne 1 -or (Field $normal 'X') -ne $case.Normal[0]) {throw "Wrong direction: $($case.Name)"}
    $ys = @($case.Coords | ForEach-Object {$_[1]})
    $range = $ys | Measure-Object -Minimum -Maximum
    if([Math]::Abs($range.Maximum-$range.Minimum-$case.Span) -gt 0.001) {throw 'Wrong measured span'}
}
Write-Output 'PASS: captured Type1 P-03 (1550) and both shallow Type2 side chains (900).'
$viewType = $engine.GetNestedType('ViewData', $flags)
$partType = $engine.GetNestedType('PartData', $flags)
$segmentType = $engine.GetNestedType('Segment2', $flags)
function SetField($obj, $name, $value) { $obj.GetType().GetField($name,$flags).SetValue($obj,$value) }
$angleMethod = $engine.GetMethod('BuildType2BraceAngle',$flags)
$angleCount = 0
foreach ($slope in @(0.01,0.255,2.0)) {
 foreach ($sign in @(-1,1)) {
  foreach ($scale in @(5.0,10.0,20.0)) {
   $view = [Activator]::CreateInstance($viewType,$true)
   SetField $view 'Scale' $scale
   $part = [Activator]::CreateInstance($partType,$true)
   $origin = Point 123 -456
   $end = Point (123+1000*$sign) (-456+1000*$slope)
   $vertices = Field $part 'Vertices'
   $vertices.Add($end); $vertices.Add($origin)
   $segment = [Activator]::CreateInstance($segmentType,$flags,$null,[object[]]@($end,$origin),$null)
   (Field $part 'Segments').Add($segment)
   $unit = [Math]::Sqrt(1+$slope*$slope)
   $axis = Point ($sign/$unit) ($slope/$unit)
   $a = $angleMethod.Invoke($null,[object[]]@($view,$part,$axis,'test-angle'))
   if ([Math]::Abs((Field $a 'Degrees')-[Math]::Atan($slope)*180/[Math]::PI) -gt 0.000001) { throw 'Wrong slope angle' }
   if ((Field (Field $a 'Point2') 'Y') -ne -456) { throw 'Angle baseline not horizontal' }
   if ((Field (Field $a 'Origin') 'X') -ne 123) { throw 'Wrong angle origin' }
   $angleCount++
  }
 }
}
Write-Output "PASS: $angleCount angle geometry cases (shallow/steep, mirrored, translated, scales 5/10/20)."
$spacing = $engine.GetMethod('SpaceType2InnerChain',$flags)
foreach ($scale in @(5.0,10.0,20.0)) {
 $view = [Activator]::CreateInstance($viewType,$true); SetField $view 'Scale' $scale
 $plans = [Activator]::CreateInstance($listType)
 foreach ($entry in @(@('inner','MainMajorInner',30.0),@('middle','CrossMajorInner',0.0),@('outer','CrossMajorOuter',0.0))) {
  $p = [Activator]::CreateInstance($planType,$true)
  SetField $p 'Name' $entry[0]; SetField $p 'Tier' ([Enum]::Parse($tierType,$entry[1]))
  SetField $p 'View' $view; SetField $p 'PlacementNormal' (Point 0 1)
  (Field $p 'Points').Add((Point 123 $entry[2])); $plans.Add($p)
 }
 $null = $spacing.Invoke($null,[object[]]@($plans,'inner','middle','outer'))
 $d0 = $planType.GetProperty('Distance').GetValue($plans[0],$null)+30
 $d1 = $planType.GetProperty('Distance').GetValue($plans[1],$null)
 $d2 = $planType.GetProperty('Distance').GetValue($plans[2],$null)
 if ([Math]::Abs(($d2-$d1)-($d1-$d0)) -gt 0.000001) { throw 'Unequal visual tier gaps' }
}
Write-Output 'PASS: equal visual tier gaps with different anchors at three scales.'
$vertexMethod = $engine.GetMethod('BuildBraceAngleAtVertex',$flags)
foreach ($sign in @(-1,1)) {
 $view = [Activator]::CreateInstance($viewType,$true); SetField $view 'Scale' 10.0
 $part = [Activator]::CreateInstance($partType,$true)
 $origin = Point 123 456
 $end = Point (123+1000*$sign) -44
 $segment = [Activator]::CreateInstance($segmentType,$flags,$null,[object[]]@($end,$origin),$null)
 (Field $part 'Segments').Add($segment)
 $axis = Point ($sign/[Math]::Sqrt(1.25)) (-0.5/[Math]::Sqrt(1.25))
 $a = $vertexMethod.Invoke($null,[object[]]@($view,$part,$axis,'upper-type1',$origin,$true))
 if ([Math]::Abs((Field $a 'Degrees')-63.434948822922) -gt 0.000001) { throw 'Wrong vertical complement' }
 if ((Field (Field $a 'Point2') 'X') -ne 123 -or (Field (Field $a 'Point2') 'Y') -ge 456) { throw 'Wrong downward baseline' }
}
Write-Output 'PASS: Type1 upper brace uses downward vertical, including mirrored geometry.'
