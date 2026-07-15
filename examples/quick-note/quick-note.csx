// 📝 快速笔记 - 快捷键弹出笔记窗口
//
// 功能：Ctrl+Shift+N 打开笔记窗口
// 能力：storage, window, hotkey
// 作者：Long_BetterWindows 示例

using System;
using System.Text;
using System.Collections.Generic;

// 笔记数据结构
class Note
{
    public string Id { get; set; } = "";
    public string Content { get; set; } = "";
    public long Created { get; set; }
}

// 加载笔记
async Task<List<Note>> LoadNotes()
{
    var notes = await Host.Storage.GetAsync("notes");
    if (notes == null) return new List<Note>();

    return System.Text.Json.JsonSerializer.Deserialize<List<Note>>(notes.ToString());
}

// 保存笔记
async Task SaveNotes(List<Note> notes)
{
    var json = System.Text.Json.JsonSerializer.Serialize(notes);
    await Host.Storage.SetAsync("notes", json);
}

// 创建笔记窗口 HTML
string CreateNoteHtml(List<Note> notes)
{
    var html = new StringBuilder();
    html.Append(@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <style>
        body {
            font-family: 'Microsoft YaHei', sans-serif;
            margin: 0;
            padding: 20px;
            background: #1e1f22;
            color: #f8fafc;
        }
        .header {
            display: flex;
            justify-content: space-between;
            align-items: center;
            margin-bottom: 20px;
        }
        h2 { margin: 0; }
        button {
            background: #3b82f6;
            color: white;
            border: none;
            padding: 8px 16px;
            border-radius: 6px;
            cursor: pointer;
        }
        button:hover { background: #2563eb; }
        .note-list { display: flex; flex-direction: column; gap: 10px; }
        .note {
            background: #2d2e32;
            padding: 15px;
            border-radius: 8px;
            border: 1px solid #3e3f46;
        }
        textarea {
            width: 100%;
            min-height: 100px;
            background: #1e1f22;
            color: #f8fafc;
            border: 1px solid #3e3f46;
            border-radius: 6px;
            padding: 10px;
            font-family: inherit;
            resize: vertical;
        }
        .note-footer {
            display: flex;
            justify-content: space-between;
            margin-top: 10px;
            font-size: 12px;
            color: #94a3b8;
        }
    </style>
</head>
<body>
    <div class='header'>
        <h2>📝 快速笔记</h2>
        <button onclick='addNote()'>+ 新建</button>
    </div>
    <div class='note-list' id='noteList'></div>

    <script>
        let notes = " + System.Text.Json.JsonSerializer.Serialize(notes) + @";

        function renderNotes() {
            const container = document.getElementById('noteList');
            container.innerHTML = notes.map(note => `
                <div class='note'>
                    <textarea onchange='updateNote(""${note.Id}"", this.value)'>${note.Content}</textarea>
                    <div class='note-footer'>
                        <span>${new Date(note.Created).toLocaleString('zh-CN')}</span>
                        <button onclick='deleteNote(""${note.Id}"")'>删除</button>
                    </div>
                </div>
            `).join('');
        }

        function addNote() {
            const note = {
                Id: Date.now().toString(),
                Content: '',
                Created: Date.now()
            };
            notes.unshift(note);
            renderNotes();
        }

        function updateNote(id, content) {
            const note = notes.find(n => n.Id === id);
            if (note) note.Content = content;
        }

        function deleteNote(id) {
            notes = notes.filter(n => n.Id !== id);
            renderNotes();
        }

        renderNotes();
    </script>
</body>
</html>
");
    return html.ToString();
}

// 注册热键
await Host.HotKey.RegisterAsync("Ctrl+Shift+N", async () =>
{
    var notes = await LoadNotes();
    var html = CreateNoteHtml(notes);

    var windowId = await Host.UI.CreateWindowAsync(
        "快速笔记",
        html,
        600,
        500,
        true
    );

    await Host.Notification.ShowAsync("📝 笔记窗口已打开", "info");
});

Console.WriteLine("✅ 快速笔记已加载 - 按 Ctrl+Shift+N 打开笔记");
