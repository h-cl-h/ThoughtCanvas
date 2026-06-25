using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;

namespace ThoughtCanvas
{
    // 读写 .bmap（与 Electron 版同一格式）。整体读写 Document：
    // textboxes/braces 还原大括号树，rbraces/fgroups 暂存透传，links=蜘蛛网，
    // boundaries/summaries/callouts/relations=叠加层，view 保存编号/紧凑/缩放/聚焦。
    public static class BmapIO
    {
        // 单文件/裁剪发布时，JsonSerializerOptions 必须显式带上 TypeInfoResolver，否则写 JSON 会抛异常
        static readonly JsonSerializerOptions WriteOpts = new JsonSerializerOptions
        {
            WriteIndented = true,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver()
        };

        // ---------- 读取 ----------
        public static Document Load(string text)
        {
            var d = new Document();
            using var doc = JsonDocument.Parse(text);
            var r = doc.RootElement;

            string S(JsonElement e, string k, string def = "")
                => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : def;
            double N(JsonElement e, string k, double def = 0)
                => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : def;
            bool B(JsonElement e, string k)
                => e.TryGetProperty(k, out var v) && (v.ValueKind == JsonValueKind.True);

            if (r.TryGetProperty("docType", out var dt) && dt.ValueKind == JsonValueKind.String) d.DocType = dt.GetString();
            d.Name = S(r, "name", "未命名思维导图");
            if (string.IsNullOrWhiteSpace(d.Name)) d.Name = "未命名思维导图";

            if (!r.TryGetProperty("textboxes", out var tbEl)) return d;

            var topics = new Dictionary<string, Topic>();
            var tbBrace = new Dictionary<string, string>();   // 文本框 -> 它自己的大括号 id
            foreach (var p in tbEl.EnumerateObject())
            {
                var t = new Topic { Id = p.Name };
                var v = p.Value;
                t.Text = S(v, "text");
                t.Bg   = S(v, "bg");
                t.X    = N(v, "x");
                t.Y    = N(v, "y");
                t.Note = S(v, "note");
                t.Link = S(v, "link");
                t.Todo = B(v, "todo");
                t.Done = B(v, "done");
                if (v.TryGetProperty("markers", out var mk) && mk.ValueKind == JsonValueKind.Array)
                    foreach (var m in mk.EnumerateArray()) if (m.ValueKind == JsonValueKind.String) t.Markers.Add(m.GetString());
                if (v.TryGetProperty("tags", out var tg) && tg.ValueKind == JsonValueKind.Array)
                    foreach (var m in tg.EnumerateArray()) if (m.ValueKind == JsonValueKind.String) t.Tags.Add(m.GetString());
                if (v.TryGetProperty("anchors", out var an) && an.ValueKind == JsonValueKind.Array)
                    foreach (var m in an.EnumerateArray()) if (m.ValueKind == JsonValueKind.Number) t.Anchors.Add(m.GetDouble());
                if (v.TryGetProperty("brace", out var bv) && bv.ValueKind == JsonValueKind.String) tbBrace[p.Name] = bv.GetString();
                topics[p.Name] = t;
            }

            var braceChildren = new Dictionary<string, List<string>>();
            if (r.TryGetProperty("braces", out var brEl))
            {
                foreach (var p in brEl.EnumerateObject())
                {
                    var list = new List<string>();
                    if (p.Value.TryGetProperty("children", out var ch) && ch.ValueKind == JsonValueKind.Array)
                        foreach (var c in ch.EnumerateArray())
                            if (c.ValueKind == JsonValueKind.String) list.Add(c.GetString());
                    braceChildren[p.Name] = list;
                }
            }

            // 连子节点（每个主题最多一个父，跳过自指，避免环导致死循环/崩溃）
            var hasParent = new HashSet<Topic>();
            foreach (var kv in tbBrace)
            {
                if (!topics.TryGetValue(kv.Key, out var parent)) continue;
                if (!braceChildren.TryGetValue(kv.Value, out var kids)) continue;
                foreach (var cid in kids)
                {
                    if (!topics.TryGetValue(cid, out var ct)) continue;
                    if (ct == parent || hasParent.Contains(ct)) continue;
                    parent.Children.Add(ct);
                    hasParent.Add(ct);
                }
            }

            if (r.TryGetProperty("roots", out var rootsEl) && rootsEl.ValueKind == JsonValueKind.Array)
                foreach (var rid in rootsEl.EnumerateArray())
                    if (rid.ValueKind == JsonValueKind.String && topics.TryGetValue(rid.GetString(), out var rt))
                        d.Roots.Add(rt);

            // 蜘蛛网模式：roots 可能为空，把所有无父节点都当作自由节点
            if (d.DocType == "spider")
            {
                d.Roots.Clear();
                foreach (var p in tbEl.EnumerateObject())
                    if (topics.TryGetValue(p.Name, out var t) && !hasParent.Contains(t))
                        d.Roots.Add(t);
            }

            // —— 叠加层 ——
            if (r.TryGetProperty("boundaries", out var bdEl) && bdEl.ValueKind == JsonValueKind.Object)
                foreach (var p in bdEl.EnumerateObject())
                {
                    var b = new Boundary { Id = p.Name, Label = S(p.Value, "label"), Color = S(p.Value, "color") };
                    if (p.Value.TryGetProperty("members", out var ms) && ms.ValueKind == JsonValueKind.Array)
                        foreach (var m in ms.EnumerateArray()) if (m.ValueKind == JsonValueKind.String) b.Members.Add(m.GetString());
                    d.Boundaries.Add(b);
                }
            if (r.TryGetProperty("summaries", out var smEl) && smEl.ValueKind == JsonValueKind.Object)
                foreach (var p in smEl.EnumerateObject())
                {
                    var s = new Summary { Id = p.Name, Text = S(p.Value, "text") };
                    if (p.Value.TryGetProperty("members", out var ms) && ms.ValueKind == JsonValueKind.Array)
                        foreach (var m in ms.EnumerateArray()) if (m.ValueKind == JsonValueKind.String) s.Members.Add(m.GetString());
                    d.Summaries.Add(s);
                }
            if (r.TryGetProperty("callouts", out var coEl) && coEl.ValueKind == JsonValueKind.Object)
                foreach (var p in coEl.EnumerateObject())
                    d.Callouts.Add(new Callout { Id = p.Name, Tb = S(p.Value, "tb"), Text = S(p.Value, "text"), Color = S(p.Value, "color") });
            if (r.TryGetProperty("relations", out var rlEl) && rlEl.ValueKind == JsonValueKind.Object)
                foreach (var p in rlEl.EnumerateObject())
                    d.Relations.Add(new Relation { Id = p.Name, A = S(p.Value, "a"), B = S(p.Value, "b"), Text = S(p.Value, "text") });

            // —— 蜘蛛网连线 ——
            if (r.TryGetProperty("links", out var lkEl) && lkEl.ValueKind == JsonValueKind.Object)
                foreach (var p in lkEl.EnumerateObject())
                    d.Links.Add(new Link
                    {
                        Id = p.Name, A = S(p.Value, "a"), B = S(p.Value, "b"),
                        AAng = N(p.Value, "aAng"), BAng = N(p.Value, "bAng"),
                        Mode = S(p.Value, "mode", "curve"), Text = S(p.Value, "text"),
                        Tx = N(p.Value, "tx"), Ty = N(p.Value, "ty"), Dist = N(p.Value, "dist")
                    });

            // —— view ——
            if (r.TryGetProperty("view", out var vw) && vw.ValueKind == JsonValueKind.Object)
            {
                d.Numbering   = S(vw, "numbering");
                d.CompactMode = (int)N(vw, "compactMode");
                d.Scale       = N(vw, "scale", 1);
                d.PanX        = N(vw, "panX");
                d.PanY        = N(vw, "panY");
            }
            d.FocusId = S(r, "focusId", null);
            return d;
        }

        // ---------- 写出 ----------
        public static string Save(Document d)
        {
            var textboxes = new JsonObject();
            var braces = new JsonObject();
            var rootIds = new JsonArray();
            int bc = 0;

            JsonObject WriteTb(Topic t, string parentBraceId, string braceId, bool free)
            {
                var tb = new JsonObject
                {
                    ["id"] = t.Id,
                    ["text"] = t.Text,
                    ["locked"] = false,
                    ["parentBrace"] = parentBraceId,
                    ["brace"] = braceId,
                    ["x"] = free ? t.X : 0,
                    ["y"] = free ? t.Y : 0,
                    ["todo"] = t.Todo,
                    ["done"] = t.Done
                };
                if (!string.IsNullOrEmpty(t.Bg)) tb["bg"] = t.Bg;
                if (!string.IsNullOrEmpty(t.Note)) tb["note"] = t.Note;
                if (!string.IsNullOrEmpty(t.Link)) tb["link"] = t.Link;
                if (t.Markers.Count > 0) { var a = new JsonArray(); foreach (var m in t.Markers) a.Add(m); tb["markers"] = a; }
                if (t.Tags.Count > 0)    { var a = new JsonArray(); foreach (var m in t.Tags)    a.Add(m); tb["tags"] = a; }
                if (t.Anchors.Count > 0) { var a = new JsonArray(); foreach (var m in t.Anchors) a.Add(m); tb["anchors"] = a; }
                return tb;
            }

            bool spider = d.DocType == "spider";
            string Walk(Topic t, string parentBraceId, bool isRoot)
            {
                string braceId = t.Children.Count > 0 ? "b" + (++bc) : null;
                bool free = isRoot || spider;
                textboxes[t.Id] = WriteTb(t, parentBraceId, braceId, free);
                if (braceId != null)
                {
                    var childIds = new JsonArray();
                    foreach (var c in t.Children) childIds.Add(Walk(c, braceId, false));
                    braces[braceId] = new JsonObject
                    {
                        ["id"] = braceId, ["locked"] = false, ["parentTb"] = t.Id, ["children"] = childIds
                    };
                }
                return t.Id;
            }

            foreach (var rt in d.Roots) rootIds.Add(Walk(rt, null, true));

            var boundaries = new JsonObject();
            foreach (var b in d.Boundaries)
            {
                var mem = new JsonArray(); foreach (var m in b.Members) mem.Add(m);
                boundaries[b.Id] = new JsonObject { ["id"] = b.Id, ["members"] = mem, ["label"] = b.Label, ["color"] = b.Color };
            }
            var summaries = new JsonObject();
            foreach (var s in d.Summaries)
            {
                var mem = new JsonArray(); foreach (var m in s.Members) mem.Add(m);
                summaries[s.Id] = new JsonObject { ["id"] = s.Id, ["members"] = mem, ["text"] = s.Text };
            }
            var callouts = new JsonObject();
            foreach (var c in d.Callouts)
                callouts[c.Id] = new JsonObject { ["id"] = c.Id, ["tb"] = c.Tb, ["text"] = c.Text, ["color"] = c.Color };
            var relations = new JsonObject();
            foreach (var rl in d.Relations)
                relations[rl.Id] = new JsonObject { ["id"] = rl.Id, ["a"] = rl.A, ["b"] = rl.B, ["text"] = rl.Text };
            var links = new JsonObject();
            foreach (var l in d.Links)
                links[l.Id] = new JsonObject
                {
                    ["id"] = l.Id, ["a"] = l.A, ["aAng"] = l.AAng, ["b"] = l.B, ["bAng"] = l.BAng,
                    ["locked"] = false, ["mode"] = l.Mode, ["text"] = l.Text, ["tx"] = l.Tx, ["ty"] = l.Ty, ["dist"] = l.Dist
                };

            var obj = new JsonObject
            {
                ["app"] = "brace-mindmap",
                ["version"] = 1,
                ["docType"] = d.DocType,
                ["name"] = d.Name,
                ["idc"] = textboxes.Count + bc,
                ["roots"] = rootIds,
                ["textboxes"] = textboxes,
                ["braces"] = braces,
                ["rbraces"] = new JsonObject(),
                ["fgroups"] = new JsonObject(),
                ["links"] = links,
                ["boundaries"] = boundaries,
                ["summaries"] = summaries,
                ["callouts"] = callouts,
                ["relations"] = relations,
                ["focusId"] = d.FocusId,
                ["view"] = new JsonObject
                {
                    ["scale"] = d.Scale, ["panX"] = d.PanX, ["panY"] = d.PanY,
                    ["compactMode"] = d.CompactMode, ["numbering"] = d.Numbering
                }
            };
            return obj.ToJsonString(WriteOpts);
        }
    }
}
