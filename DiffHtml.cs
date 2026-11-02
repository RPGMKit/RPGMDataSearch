using System.Net;
using System.Text;
using DiffPlex;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;

namespace RPGMDataSearch;

/// <summary>
/// The diff of two texts as one html page, in the form of <c>git diff</c> with the colors of the hosting sites: the lines of the changes with three lines of context around them and a header
/// <c>@@ -12,5 +12,6 @@</c> for each piece, and the words that differ marked inside the changed lines. The page has both views, unified and side by side, and a switch between them.
/// </summary>
internal static class DiffHtml
{
    private const int Context = 3;

    // a line of the unified view: the side of the change, the numbers it has in the old and the new text, and its html
    private sealed record Line(char Sign, int? Old, int? New, string Html);

    // a row of the side by side view
    private sealed record Row(DiffPiece Old, DiffPiece New);

    public static string Build(IReadOnlyList<string> left, IReadOnlyList<string> right, string leftTitle, string rightTitle)
    {
        var model = new SideBySideDiffBuilder(new Differ()).BuildDiffModel(string.Join('\n', left), string.Join('\n', right), ignoreWhitespace: false);
        var rows = model.OldText.Lines.Zip(model.NewText.Lines, (old, @new) => new Row(old, @new)).ToList();
        var unified = Unified(rows);
        var added = unified.Count(line => line.Sign == '+');
        var removed = unified.Count(line => line.Sign == '-');

        var sb = new StringBuilder();
        sb.Append("<!DOCTYPE html><html><head><meta charset=\"utf-8\"><title>").Append(WebUtility.HtmlEncode($"Diff: {leftTitle} / {rightTitle}")).Append("</title><style>").Append(Css).Append("</style></head><body>");
        sb.Append("<div class=\"bar\"><span class=\"title\"><span id=\"lt\" class=\"del-t\">").Append(WebUtility.HtmlEncode(leftTitle)).Append("</span> &rarr; <span id=\"rt\" class=\"add-t\">")
            .Append(WebUtility.HtmlEncode(rightTitle)).Append("</span></span><button id=\"swap\" class=\"swap\" title=\"Swap the sides: what was removed becomes added\">Swap sides</button>");
        sb.Append($"<span class=\"stat\"><span id=\"sa\" class=\"add-t\">+{added}</span> <span id=\"sd\" class=\"del-t\">&minus;{removed}</span></span>");
        sb.Append("<span class=\"views\"><button data-view=\"unified\">Unified</button><button data-view=\"split\">Side by side</button></span>");
        sb.Append("<label><input type=\"checkbox\" id=\"all\"> Show all lines</label></div>");
        AppendUnified(sb, unified);
        AppendSplit(sb, rows);
        sb.Append("<script>").Append(Script).Append("</script></body></html>");
        return sb.ToString();
    }

    // ---- the text of a line: the words that differ are marked ----

    private static string Text(DiffPiece piece)
    {
        if (piece.Type == ChangeType.Modified && piece.SubPieces.Count > 0)
        {
            var sb = new StringBuilder();
            foreach (var sub in piece.SubPieces)
            {
                var text = WebUtility.HtmlEncode(sub.Text ?? "");
                sb.Append(sub.Type is ChangeType.Deleted or ChangeType.Inserted ? $"<mark>{text}</mark>" : text);
            }

            return sb.ToString();
        }

        return WebUtility.HtmlEncode(piece.Text ?? "");
    }

    // ---- unified ----

    // the lines of the changes: a block of changed rows is its deleted lines and then its inserted ones, as git writes it
    private static List<Line> Unified(List<Row> rows)
    {
        var lines = new List<Line>();
        var at = 0;
        while (at < rows.Count)
        {
            if (rows[at].Old.Type == ChangeType.Unchanged)
            {
                lines.Add(new(' ', rows[at].Old.Position, rows[at].New.Position, Text(rows[at].Old)));
                at++;
                continue;
            }

            var removed = new List<Line>();
            var added = new List<Line>();
            for (; at < rows.Count && rows[at].Old.Type != ChangeType.Unchanged; at++)
            {
                if (rows[at].Old.Type != ChangeType.Imaginary)
                    removed.Add(new('-', rows[at].Old.Position, null, Text(rows[at].Old)));
                if (rows[at].New.Type != ChangeType.Imaginary)
                    added.Add(new('+', null, rows[at].New.Position, Text(rows[at].New)));
            }

            lines.AddRange(removed);
            lines.AddRange(added);
        }

        return lines;
    }

    // whether each item is within the context of a change (the others are hidden unless all the lines are shown)
    private static bool[] Near(IReadOnlyList<bool> changed)
    {
        var near = new bool[changed.Count];
        for (var i = 0; i < changed.Count; i++)
        {
            if (!changed[i])
                continue;

            for (var j = Math.Max(0, i - Context); j <= Math.Min(changed.Count - 1, i + Context); j++)
                near[j] = true;
        }

        return near;
    }

    private static void AppendUnified(StringBuilder sb, List<Line> lines)
    {
        var near = Near(lines.Select(line => line.Sign != ' ').ToList());
        sb.Append("<table class=\"u\">");
        // (the counters hold the last line that was written of each text: the number a piece starts at is the next one)
        int oldNo = 0, newNo = 0;
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            if (near[i] && (i == 0 || !near[i - 1]))
            {
                // a piece: its header counts the lines of it
                var end = i;
                while (end + 1 < lines.Count && near[end + 1])
                    end++;

                var oldCount = lines.Skip(i).Take(end - i + 1).Count(l => l.Sign != '+');
                var newCount = lines.Skip(i).Take(end - i + 1).Count(l => l.Sign != '-');
                sb.Append($"<tr class=\"hh\"><td class=\"n\"></td><td class=\"n\"></td><td class=\"t\">@@ -{(oldCount == 0 ? oldNo : oldNo + 1)},{oldCount} +{(newCount == 0 ? newNo : newNo + 1)},{newCount} @@</td></tr>");
            }

            if (line.Sign != '+')
                oldNo++;
            if (line.Sign != '-')
                newNo++;

            var kind = line.Sign switch { '+' => "add", '-' => "del", _ => "ctx" };
            sb.Append($"<tr class=\"{kind}{(near[i] ? "" : " far")}\"><td class=\"n\">{line.Old}</td><td class=\"n\">{line.New}</td><td class=\"t\"><span class=\"sign\">{(line.Sign == ' ' ? "&nbsp;" : line.Sign)}</span>{line.Html}</td></tr>");
        }

        sb.Append("</table>");
    }

    // ---- side by side ----

    private static void AppendSplit(StringBuilder sb, List<Row> rows)
    {
        var near = Near(rows.Select(row => row.Old.Type != ChangeType.Unchanged).ToList());
        // (the widths of the columns are told here: the first row of a table that is laid out at once is the header of a piece, which has none)
        sb.Append("<table class=\"s\"><colgroup><col class=\"cn\"><col><col class=\"cn\"><col></colgroup>");
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var hidden = near[i] ? "" : " far";
            if (near[i] && (i == 0 || !near[i - 1]))
                sb.Append("<tr class=\"hh\"><td colspan=\"4\" class=\"t\">&hellip;</td></tr>");

            sb.Append($"<tr class=\"{hidden.Trim()}\">");
            Cell(sb, row.Old, "del");
            Cell(sb, row.New, "add");
            sb.Append("</tr>");
        }

        sb.Append("</table>");
    }

    private static void Cell(StringBuilder sb, DiffPiece piece, string changedClass)
    {
        var kind = piece.Type switch
        {
            ChangeType.Unchanged => "ctx",
            ChangeType.Imaginary => "none",
            _ => changedClass,
        };

        sb.Append($"<td class=\"n {kind}\">{piece.Position}</td><td class=\"t {kind}\">{(piece.Type == ChangeType.Imaginary ? "" : Text(piece))}</td>");
    }

    private const string Css = """
        :root{--bg:#fff;--fg:#1f2328;--muted:#656d76;--bar:#f6f8fa;--line:#d0d7de;--del:#ffebe9;--del-n:#ffd7d5;--del-m:#ff818266;--add:#dafbe1;--add-n:#ccffd8;--add-m:#4ac26b66;--hh:#ddf4ff;--none:#f6f8fa;--ctx-n:#f6f8fa}
        @media (prefers-color-scheme:dark){:root{--bg:#0d1117;--fg:#e6edf3;--muted:#8d96a0;--bar:#161b22;--line:#30363d;--del:#3c1618;--del-n:#4c1a1e;--del-m:#f8514966;--add:#12261e;--add-n:#16382a;--add-m:#2ea04366;--hh:#132d4b;--none:#161b22;--ctx-n:#161b22}}
        body{margin:0;background:var(--bg);color:var(--fg);font:13px/1.45 Consolas,monospace}
        .bar{position:sticky;top:0;z-index:2;display:flex;flex-wrap:wrap;align-items:center;gap:12px;padding:8px 12px;background:var(--bar);border-bottom:1px solid var(--line)}
        .title{font-weight:bold}.stat{margin-left:auto}
        .del-t{color:#cf222e}.add-t{color:#1a7f37}
        @media (prefers-color-scheme:dark){.del-t{color:#ff7b72}.add-t{color:#56d364}}
        .swap,.views button{padding:2px 10px;border:1px solid var(--line);background:var(--bg);color:var(--fg);cursor:pointer}
        .swap{border-radius:6px}
        .views button:first-child{border-radius:6px 0 0 6px}.views button:last-child{border-radius:0 6px 6px 0;border-left:0}
        body[data-view=unified] .views [data-view=unified],body[data-view=split] .views [data-view=split]{background:var(--hh);font-weight:bold}
        table{border-collapse:collapse;width:100%}
        td{padding:0 8px;vertical-align:top;white-space:pre-wrap;overflow-wrap:anywhere}
        td.n{width:1%;min-width:3.2em;text-align:right;color:var(--muted);user-select:none;white-space:nowrap}
        .u .sign{display:inline-block;width:1.2em;user-select:none}
        .u tr.add td.t,.s td.t.add{background:var(--add)}.u tr.add td.n,.s td.n.add{background:var(--add-n)}
        .u tr.del td.t,.s td.t.del{background:var(--del)}.u tr.del td.n,.s td.n.del{background:var(--del-n)}
        .u tr.ctx td.n,.s td.n.ctx{background:var(--ctx-n)}
        .s td.none{background:var(--none)}
        tr.hh td{background:var(--hh);color:var(--muted)}
        .s{table-layout:fixed}.s col.cn{width:3.8em}.s td.t.ctx+td.n,.s td.t.del+td.n,.s td.t.add+td.n,.s td.t.none+td.n{border-left:1px solid var(--line)}
        mark{background:var(--del-m);color:inherit;border-radius:3px}.add mark,.s td.t.add mark{background:var(--add-m)}
        body[data-view=unified] .s,body[data-view=split] .u{display:none}
        body:not(.all) tr.far{display:none}body.all tr.hh{display:none}
        """;

    private const string Script = """
        var body=document.body,all=document.getElementById('all');
        function setView(v){body.setAttribute('data-view',v);try{localStorage.setItem('rpgmdiff-view',v);}catch(e){}}
        function setAll(on){body.classList.toggle('all',on);all.checked=on;try{localStorage.setItem('rpgmdiff-all',on?'1':'0');}catch(e){}}
        var view='unified',on=false;
        try{view=localStorage.getItem('rpgmdiff-view')||view;on=localStorage.getItem('rpgmdiff-all')==='1';}catch(e){}
        setView(view);setAll(on);
        document.querySelectorAll('.views button').forEach(function(b){b.onclick=function(){setView(b.getAttribute('data-view'));};});
        all.onchange=function(){setAll(all.checked);};
        // The sides are swapped in the page: the diff of B against A is the one of A against B turned over (what was removed is added), so nothing is made again.
        function swapClass(el,a,b){var x=el.classList.contains(a),y=el.classList.contains(b);el.classList.toggle(a,y);el.classList.toggle(b,x);}
        function swapText(a,b){var t=a.innerHTML;a.innerHTML=b.innerHTML;b.innerHTML=t;}
        function swapUnified(){
          var rows=[].slice.call(document.querySelector('table.u').rows);
          for(var i=0;i<rows.length;){
            var r=rows[i];
            if(r.classList.contains('hh')){var c=r.querySelector('.t');c.textContent=c.textContent.replace(/@@ (-\S+) (\+\S+) @@/,function(m,o,n){return '@@ -'+n.slice(1)+' +'+o.slice(1)+' @@';});i++;continue;}
            if(!r.classList.contains('add')&&!r.classList.contains('del')){var k=r.querySelectorAll('td.n');swapText(k[0],k[1]);i++;continue;}   // (a line that is the same has the numbers of both texts)
            // a block of changes is its removed lines and then its added ones; turned over, the added ones (now removed) come first
            var run=[];
            while(i<rows.length&&(rows[i].classList.contains('add')||rows[i].classList.contains('del')))run.push(rows[i++]);
            var dels=run.filter(function(x){return x.classList.contains('del');}),adds=run.filter(function(x){return x.classList.contains('add');});
            var parent=run[0].parentNode,anchor=run[run.length-1].nextSibling;
            run.forEach(function(x){
              swapClass(x,'add','del');
              var n=x.querySelectorAll('td.n');swapText(n[0],n[1]);
              var s=x.querySelector('.sign');s.textContent=s.textContent==='+'?'-':'-'===s.textContent?'+':s.textContent;
            });
            adds.concat(dels).forEach(function(x){parent.insertBefore(x,anchor);});
          }
        }
        function swapSplit(){
          [].forEach.call(document.querySelector('table.s').rows,function(r){
            if(r.classList.contains('hh'))return;
            var c=[].slice.call(r.cells);r.insertBefore(c[2],c[0]);r.insertBefore(c[3],c[0]);   // (the cells are taken first: the collection of a row changes as they move)
            [].forEach.call(r.cells,function(td){swapClass(td,'add','del');});
          });
        }
        document.getElementById('swap').onclick=function(){
          swapUnified();swapSplit();
          swapText(document.getElementById('lt'),document.getElementById('rt'));
          swapText(document.getElementById('sa'),document.getElementById('sd'));
          var a=document.getElementById('sa'),d=document.getElementById('sd');
          a.textContent=a.textContent.replace(/[+\u2212]/,'+');d.textContent=d.textContent.replace(/[+\u2212]/,'\u2212');
          document.title='Diff: '+document.getElementById('lt').textContent+' / '+document.getElementById('rt').textContent;
        };
        """;
}
