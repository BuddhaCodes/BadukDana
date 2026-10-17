using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Hoshi.Core;
using Hoshi.Core.Localization;
using Hoshi.Sgf;
using Hoshi.Sgf.Study;
using Point = Hoshi.Core.Point;

namespace Hoshi.App.Services.Export;

/// <summary>What people see of the players and of Hoshi in what is shared.</summary>
public sealed record ShareOptions(bool HideNames = false, bool ShowBrand = true)
{
    public string Black(GameTree tree) => HideNames || string.IsNullOrWhiteSpace(tree.Info.BlackPlayer) ? Tr.T("Common.Black") : tree.Info.BlackPlayer!;

    public string White(GameTree tree) => HideNames || string.IsNullOrWhiteSpace(tree.Info.WhitePlayer) ? Tr.T("Common.White") : tree.Info.WhitePlayer!;
}

/// <summary>
/// The game as one web page that works offline in any browser (a blog, a club's site, a message): every position of
/// every variation is worked out here with Hoshi's rules and written into the page, with the comments and the
/// study notes; a small script of Hoshi's own steps through them (buttons, arrow keys, the variations under each
/// move). No external files, no tracking.
/// </summary>
public static class WebViewer
{
    public static string Html(GameTree tree, GameNode? start, ShareOptions options) => Html(tree, start, options, null);

    /// <summary>
    /// The page as an animated replay (the replay maker's web version): it opens on a title, then plays the steps
    /// by itself at their own pace (notes and mistakes stay longer), with play/pause; the viewer stays free to browse.
    /// </summary>
    public static string Html(GameTree tree, GameNode? start, ShareOptions options, IReadOnlyList<(GameNode Node, TimeSpan Duration, string? Highlight, ReplayMoment Moment)>? replay)
    {
        ArgumentNullException.ThrowIfNull(tree);
        ArgumentNullException.ThrowIfNull(options);
        var cursor = new GameCursor(tree);
        int size = cursor.BoardSize;
        var nodes = new List<GameNode>();
        var index = new Dictionary<GameNode, int>();
        var stack = new Stack<GameNode>();
        stack.Push(tree.Root);
        while (stack.Count > 0)
        {
            GameNode node = stack.Pop();
            index[node] = nodes.Count;
            nodes.Add(node);
            for (int i = node.Children.Count - 1; i >= 0; i--)
            {
                stack.Push(node.Children[i]);
            }
        }

        var data = new List<object>(nodes.Count);
        foreach (GameNode node in nodes)
        {
            BoardState board = cursor.GetBoard(node);
            var cells = new StringBuilder(board.Width * board.Height);
            for (int y = 0; y < board.Height; y++)
            {
                for (int x = 0; x < board.Width; x++)
                {
                    cells.Append(board[x, y] switch { Stone.Black => 'b', Stone.White => 'w', _ => '.' });
                }
            }

            Point? move = node.GetMove(size)?.Point;
            var text = new List<string>();
            if (node.GetValue("C") is { Length: > 0 } comment)
            {
                text.Add(comment);
            }

            foreach (StudyPin pin in StudyStore.Read(node).Pins)
            {
                text.Add(options.HideNames ? pin.Text : $"{pin.Author}: {pin.Text}");
                text.AddRange(pin.Replies.Select(r => options.HideNames ? "↳ " + r.Text : $"↳ {r.Author}: {r.Text}"));
            }

            data.Add(new Dictionary<string, object?>
            {
                ["b"] = cells.ToString(),
                ["m"] = move is { } p ? (p.Y * board.Width) + p.X : -1,
                ["n"] = GameCursor.Path(node).Count(n => n.HasMove),
                ["p"] = node.Parent is { } parent ? index[parent] : -1,
                ["k"] = node.Children.Select(c => index[c]).ToArray(),
                ["c"] = string.Join("\n\n", text),
                ["l"] = move is { } q ? q.ToHuman(board.Height) : node.HasMove ? Tr.T("Viewer.Pass") : string.Empty,
                ["cb"] = board.BlackCaptures,
                ["cw"] = board.WhiteCaptures,
            });
        }

        string black = options.Black(tree), white = options.White(tree);
        string title = $"{black} — {white}";
        var meta = new List<string>();
        if (tree.Info.Result is { Length: > 0 } result)
        {
            meta.Add(result);
        }

        if (tree.Info.Komi is { } komi)
        {
            meta.Add(string.Create(CultureInfo.InvariantCulture, $"komi {komi:0.#}"));
        }

        if (!options.HideNames && tree.Info.Date is { Length: > 0 } date)
        {
            meta.Add(date);
        }

        var json = new JsonSerializerOptions { Encoder = JavaScriptEncoder.Default };
        string game = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["w"] = tree.Info.Width,
            ["h"] = tree.Info.Height,
            ["start"] = start is not null && index.TryGetValue(start, out int s) ? s : 0,
            ["play"] = replay is null ? null : replay.Where(r => index.ContainsKey(r.Node)).Select(r => new Dictionary<string, object?>
            {
                ["n"] = index[r.Node],
                ["ms"] = (int)r.Duration.TotalMilliseconds,
                ["h"] = r.Highlight,
                ["f"] = r.Moment switch { ReplayMoment.Brilliant or ReplayMoment.Strong => "good", ReplayMoment.Mistake or ReplayMoment.Blunder => "bad", _ => null },
            }).ToArray(),
            ["nodes"] = data,
            ["labels"] = new Dictionary<string, string>
            {
                ["move"] = Tr.T("Viewer.Move"),
                ["captures"] = Tr.T("Viewer.Captures"),
                ["variations"] = Tr.T("Viewer.Variations"),
                ["black"] = black,
                ["white"] = white,
                ["cols"] = Point.HumanColumns,
                ["play"] = Tr.T("Viewer.Play"),
                ["pause"] = Tr.T("Viewer.Pause"),
            },
        }, json);

        var html = new StringBuilder();
        html.Append("<!doctype html><html lang=\"").Append(Tr.Language).Append("\"><head><meta charset=\"utf-8\">")
            .Append("<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"><title>").Append(E(title)).Append("</title><style>")
            .Append(Css).Append("</style></head><body><main><header><h1>").Append(E(title)).Append("</h1><p class=\"meta\">")
            .Append(E(string.Join(" · ", meta))).Append("</p></header>")
            .Append("<div class=\"viewer\"><svg id=\"board\" role=\"img\" aria-label=\"").Append(E(Tr.T("Viewer.Board"))).Append("\"></svg>")
            .Append("<aside><p id=\"status\" aria-live=\"polite\"></p><nav>")
            .Append("<button id=\"first\" aria-label=\"").Append(E(Tr.T("Viewer.First"))).Append("\">⏮</button>")
            .Append("<button id=\"prev\" aria-label=\"").Append(E(Tr.T("Viewer.Previous"))).Append("\">◀</button>")
            .Append("<button id=\"next\" aria-label=\"").Append(E(Tr.T("Viewer.Next"))).Append("\">▶</button>")
            .Append("<button id=\"last\" aria-label=\"").Append(E(Tr.T("Viewer.Last"))).Append("\">⏭</button><button id=\"play\" hidden></button></nav><p id=\"badge\" hidden></p>")
            .Append("<div id=\"variations\"></div><div id=\"comment\"></div></aside></div>");
        if (options.ShowBrand)
        {
            html.Append("<footer>").Append(E(Tr.T("Viewer.MadeWith"))).Append("</footer>");
        }

        html.Append("</main><script type=\"application/json\" id=\"game\">").Append(game).Append("</script><script>").Append(Script).Append("</script></body></html>");
        return html.ToString();
    }

    private static string E(string text) => System.Net.WebUtility.HtmlEncode(text);

    private const string Css = """
        :root{color-scheme:light dark;--bg:#f6f3ee;--fg:#222;--muted:#666;--card:#fff;--wood:#dcb36b;--line:#4a3a1e}
        @media (prefers-color-scheme:dark){:root{--bg:#17181b;--fg:#e8e6e3;--muted:#9a9a9a;--card:#222428}}
        body{margin:0;background:var(--bg);color:var(--fg);font:16px/1.5 system-ui,-apple-system,"Segoe UI",sans-serif}
        main{max-width:1100px;margin:0 auto;padding:16px}
        h1{font-size:1.4rem;margin:.2rem 0}.meta{color:var(--muted);margin:0 0 12px}
        .viewer{display:flex;gap:16px;flex-wrap:wrap}
        svg{width:min(100%,640px);aspect-ratio:1;background:var(--wood);border-radius:6px;box-shadow:0 2px 10px #0004;touch-action:manipulation}
        aside{flex:1;min-width:240px;background:var(--card);border-radius:8px;padding:12px}
        nav{display:flex;gap:8px;margin:8px 0}button{font:inherit;padding:6px 14px;border-radius:6px;border:1px solid #8884;background:var(--bg);color:var(--fg);cursor:pointer}
        button[aria-current]{outline:2px solid #c0392b}
        #variations{display:flex;gap:6px;flex-wrap:wrap;margin:6px 0}#comment{white-space:pre-wrap;color:var(--fg)}
        #badge{display:inline-block;background:#c8432f;color:#fff;border-radius:6px;padding:4px 10px;margin:4px 0}
        #badge.good{background:#e8c36a;color:#1e1a14}
        .burst{fill:none;transform-box:fill-box;transform-origin:center;animation:burst 1.1s ease-out forwards}
        .burst.good{stroke:#f3d995;stroke-width:12}.burst.bad{stroke:#e0503a;stroke-width:14}
        @keyframes burst{from{opacity:1;transform:scale(.5)}to{opacity:0;transform:scale(6)}}
        svg.fx-good{animation:glow 1.4s ease-out}svg.fx-bad{animation:jolt .5s ease-out}
        @keyframes glow{0%{box-shadow:0 0 0 0 #e8c36a00}20%{box-shadow:0 0 48px 10px #e8c36acc}100%{box-shadow:0 2px 10px #0004}}
        @keyframes jolt{0%,100%{transform:none}15%{transform:translate(-9px,2px)}35%{transform:translate(7px,-2px)}55%{transform:translate(-4px,1px)}75%{transform:translate(2px,0)}}
        @media (prefers-reduced-motion:reduce){.burst,svg.fx-good,svg.fx-bad{animation:none}}
        footer{color:var(--muted);font-size:.85rem;margin-top:16px}
        """;

    // Hoshi's own viewer: draws the stored positions; the rules were already applied when the page was written.
    private const string Script = """
        (function(){
        var g=JSON.parse(document.getElementById('game').textContent),W=g.w,H=g.h,N=g.nodes,L=g.labels,cur=g.start;
        var svg=document.getElementById('board'),ns='http://www.w3.org/2000/svg',c=100,pad=95;
        svg.setAttribute('viewBox','0 0 '+(pad*2+c*(W-1))+' '+(pad*2+c*(H-1)));
        function el(n,a,p){var e=document.createElementNS(ns,n);for(var k in a)e.setAttribute(k,a[k]);(p||svg).appendChild(e);return e}
        var defs=el('defs',{});
        [['gb','#555','#000'],['gw','#fff','#c8c8c8']].forEach(function(d){var r=el('radialGradient',{id:d[0],cx:'35%',cy:'35%',r:'70%'},defs);el('stop',{offset:'0%','stop-color':d[1]},r);el('stop',{offset:'100%','stop-color':d[2]},r)});
        var grid=el('g',{stroke:'#4a3a1e','stroke-width':3});
        for(var i=0;i<W;i++)el('line',{x1:pad+i*c,y1:pad,x2:pad+i*c,y2:pad+c*(H-1)},grid);
        for(var j=0;j<H;j++)el('line',{x1:pad,y1:pad+j*c,x2:pad+c*(W-1),y2:pad+j*c},grid);
        var lab=el('g',{fill:'#4a3a1e','font-size':30,'text-anchor':'middle','font-family':'sans-serif'});
        for(i=0;i<W;i++){el('text',{x:pad+i*c,y:pad-62},lab).textContent=L.cols[i];el('text',{x:pad+i*c,y:pad+c*(H-1)+82},lab).textContent=L.cols[i]}
        for(j=0;j<H;j++){el('text',{x:pad-70,y:pad+j*c+10},lab).textContent=H-j;el('text',{x:pad+c*(W-1)+70,y:pad+j*c+10},lab).textContent=H-j}
        if(W===H&&W>=7){var e=W>=13?3:2,hi=W-1-e,m=(W-1)/2,st=[[e,e],[hi,e],[e,hi],[hi,hi]];if(W%2){st.push([m,m]);if(W>=15)st.push([m,e],[m,hi],[e,m],[hi,m])}
        st.forEach(function(s){el('circle',{cx:pad+s[0]*c,cy:pad+s[1]*c,r:9,fill:'#4a3a1e'})})}
        var stones=el('g',{});
        function draw(){var n=N[cur];while(stones.firstChild)stones.removeChild(stones.firstChild);
        for(var p=0;p<W*H;p++){var s=n.b[p];if(s==='.')continue;var x=pad+(p%W)*c,y=pad+Math.floor(p/W)*c;
        el('circle',{cx:x,cy:y,r:47,fill:'url(#g'+s+')',stroke:s==='w'?'#999':'none','stroke-width':2},stones);
        if(p===n.m)el('circle',{cx:x,cy:y,r:20,fill:'none',stroke:s==='b'?'#fff':'#000','stroke-width':6},stones)}
        document.getElementById('status').textContent=L.move+' '+n.n+(n.l?' ('+n.l+')':'')+' · '+L.captures+': '+L.black+' '+n.cb+', '+L.white+' '+n.cw;
        document.getElementById('comment').textContent=n.c;
        var v=document.getElementById('variations');v.innerHTML='';var par=n.p>=0?N[n.p]:null;
        if(par&&par.k.length>1){var t=document.createElement('span');t.textContent=L.variations+':';v.appendChild(t);
        par.k.forEach(function(k,i){var b=document.createElement('button');b.textContent=String.fromCharCode(65+i)+' '+N[k].l;if(k===cur)b.setAttribute('aria-current','true');b.onclick=function(){cur=k;draw()};v.appendChild(b)})}}
        function go(d){var n=N[cur];if(d<0&&n.p>=0)cur=n.p;else if(d>0&&n.k.length)cur=n.k[0];draw()}
        document.getElementById('prev').onclick=function(){go(-1)};document.getElementById('next').onclick=function(){go(1)};
        document.getElementById('first').onclick=function(){cur=0;draw()};
        document.getElementById('last').onclick=function(){while(N[cur].k.length)cur=N[cur].k[0];draw()};
        document.addEventListener('keydown',function(e){if(e.key==='ArrowLeft')go(-1);else if(e.key==='ArrowRight')go(1)});
        svg.addEventListener('click',function(e){var r=svg.getBoundingClientRect();go(e.clientX-r.left<r.width/2?-1:1)});
        var P=g.play,pi=-1,timer=null,pb=document.getElementById('play'),badge=document.getElementById('badge');
        function stop(){clearTimeout(timer);timer=null;if(P)pb.textContent='▶ '+L.play}
        function fx(f){svg.classList.remove('fx-good','fx-bad');var n=N[cur];if(!f||n.m<0)return;void svg.getBoundingClientRect();svg.classList.add('fx-'+f);
        var x=pad+(n.m%W)*c,y=pad+Math.floor(n.m/W)*c;el('circle',{cx:x,cy:y,r:30,'class':'burst '+f},stones);if(f==='bad')setTimeout(function(){if(stones.lastChild)el('circle',{cx:x,cy:y,r:30,'class':'burst bad'},stones)},160)}
        function tick(){if(pi>=P.length-1){stop();return}pi++;cur=P[pi].n;draw();badge.hidden=!P[pi].h;badge.textContent=P[pi].h||'';badge.className=P[pi].f==='good'?'good':'';fx(P[pi].f);timer=setTimeout(tick,P[pi].ms)}
        if(P&&P.length){pb.hidden=false;pb.onclick=function(){if(timer)stop();else{if(pi>=P.length-1)pi=-1;pb.textContent='❚❚ '+L.pause;tick()}};
        ['prev','next','first','last'].forEach(function(id){document.getElementById(id).addEventListener('click',stop)});
        cur=P[0].n;pi=0;draw();pb.textContent='❚❚ '+L.pause;timer=setTimeout(tick,P[0].ms)}
        else draw()})();
        """;
}
