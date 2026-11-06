# Converts an SVG path 'd' from the Material Symbols viewBox (0 -960 960 960) to a 24 x 24 box:
# x' = x / 40, y' = (y + 960) / 40. Relative commands only scale; absolute ones also translate.
# Supports M m L l H h V v C c S s Q q T t A a Z z. Any other command fails loudly.
function Format-Num([double]$v) {
    $r = [math]::Round($v, 4)
    if ($r -eq 0) { return '0' }
    return $r.ToString('0.####', [System.Globalization.CultureInfo]::InvariantCulture)
}

function ConvertTo-Material24Path([string]$d) {
    $inv = [System.Globalization.CultureInfo]::InvariantCulture
    $numRe = [regex]'\G[-+]?(?:\d*\.\d+|\d+\.?)(?:[eE][-+]?\d+)?'
    $counts = @{ M=2; L=2; H=1; V=1; C=6; S=4; Q=4; T=2; A=7; Z=0 }
    $pos = 0; $n = $d.Length
    $out = New-Object System.Text.StringBuilder
    $cmd = $null
    $isFirst = $true; $firstRel = $false
    function Skip-Sep { while ($script:pos -lt $script:n -and ($script:d[$script:pos] -eq ' ' -or $script:d[$script:pos] -eq ',' -or $script:d[$script:pos] -eq "`t" -or $script:d[$script:pos] -eq "`n" -or $script:d[$script:pos] -eq "`r")) { $script:pos++ } }
    $script:pos = 0; $script:n = $n; $script:d = $d
    while ($true) {
        Skip-Sep
        if ($script:pos -ge $n) { break }
        $ch = $d[$script:pos]
        if ([char]::IsLetter($ch)) {
            if ('MmLlHhVvCcSsQqTtAaZz'.IndexOf($ch) -lt 0) { throw "Unsupported SVG path command '$ch'" }
            $cmd = $ch; $script:pos++
            # The first 'm' of a path is absolute (the current point starts at 0,0 of the viewBox origin), so it must be translated too.
            $firstRel = $false
            if ($isFirst -and $cmd -ceq 'm') { $cmd = 'M'; $firstRel = $true }
            [void]$out.Append($cmd)
            if ($cmd -eq 'Z' -or $cmd -eq 'z') { $cmd = $null; continue }
        } elseif ($null -eq $cmd) {
            throw "Path data has numbers without a command at index $($script:pos)"
        }
        $isFirst = $false
        $up = [char]::ToUpper($cmd); $abs = ($cmd -ceq $up)
        $cnt = $counts[[string]$up]
        $nums = @()
        for ($i = 0; $i -lt $cnt; $i++) {
            Skip-Sep
            if ($up -eq 'A' -and ($i -eq 3 -or $i -eq 4)) {
                $f = $d[$script:pos]
                if ($f -ne '0' -and $f -ne '1') { throw "Bad arc flag '$f' at index $($script:pos)" }
                $nums += [double]::Parse([string]$f, $inv); $script:pos++
            } else {
                $m = $numRe.Match($d, $script:pos)
                if (-not $m.Success) { throw "Expected a number at index $($script:pos)" }
                $nums += [double]::Parse($m.Value, $inv); $script:pos += $m.Length
            }
        }
        $vals = @()
        for ($i = 0; $i -lt $cnt; $i++) {
            $v = $nums[$i]
            switch ($up) {
                'H' { $v = $v / 40; if ($abs) { } }
                'V' { $v = if ($abs) { ($v + 960) / 40 } else { $v / 40 } }
                'A' {
                    if ($i -le 1) { $v = $v / 40 }
                    elseif ($i -eq 5) { $v = $v / 40 }
                    elseif ($i -eq 6) { $v = if ($abs) { ($v + 960) / 40 } else { $v / 40 } }
                }
                default {
                    if ($i % 2 -eq 0) { $v = $v / 40 } else { $v = if ($abs) { ($v + 960) / 40 } else { $v / 40 } }
                }
            }
            $vals += (Format-Num $v)
        }
        [void]$out.Append(($vals -join ' '))
        # implicit repeat: M becomes L, m becomes l
        if ($cmd -ceq 'M') { $cmd = if ($firstRel) { 'l' } else { 'L' } } elseif ($cmd -ceq 'm') { $cmd = 'l' }
        Skip-Sep
        if ($script:pos -lt $n -and -not [char]::IsLetter($d[$script:pos])) { [void]$out.Append(' ') }
        if ($script:pos -lt $n -and [char]::IsLetter($d[$script:pos])) { continue }
        if ($script:pos -lt $n) { [void]$out.Append($cmd) }
    }
    return $out.ToString()
}
