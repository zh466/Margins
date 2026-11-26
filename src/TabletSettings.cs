using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace Polya {
    sealed partial class Workspace {
        sealed class TabletCommand {
            public readonly string Id,Title;
            public TabletCommand(string id,string title){Id=id;Title=title;}
        }
        static readonly TabletCommand[] tabletActions={
            new TabletCommand("pen","Перо"),new TabletCommand("eraser","Ластик объектов"),
            new TabletCommand("pixel","Пиксельный ластик"),new TabletCommand("select","Выделение"),
            new TabletCommand("hand","Рука"),new TabletCommand("undo","Отменить"),
            new TabletCommand("redo","Повторить"),new TabletCommand("fit","Показать всю доску"),
            new TabletCommand("study","Учебные инструменты")
        };
        public sealed class TabletProfile {
            public Dictionary<string,int> Shortcuts {get;set;}
            public bool Pressure {get;set;}
            public bool EraserEnd {get;set;}
            public TabletProfile(){Shortcuts=new Dictionary<string,int>();Pressure=EraserEnd=true;}
        }
        TabletProfile tablet=new TabletProfile();
        static string TabletPath {get{return Path.Combine(Path.GetDirectoryName(RecoveryPath),"tablet.json");}}
        internal static void WriteTabletProfile(string path,TabletProfile profile) {
            Notebook.WriteEncoded(path,new JavaScriptSerializer().Serialize(profile));
        }
        internal static TabletProfile ReadTabletProfile(string path) {
            var profile=new JavaScriptSerializer().Deserialize<TabletProfile>(File.ReadAllText(path));
            return profile!=null&&profile.Shortcuts!=null?profile:new TabletProfile();
        }
        void LoadTabletSettings() {
            if(!recoveryAllowed||!File.Exists(TabletPath))return;
            try {tablet=ReadTabletProfile(TabletPath);
            } catch(Exception) { tablet=new TabletProfile(); }
        }
        bool HandleTabletKey(KeyEventArgs e) {
            var action=tabletActions.FirstOrDefault(row=>{
                int code;return tablet.Shortcuts.TryGetValue(row.Id,out code)&&code==(int)e.KeyData&&code!=0;
            });
            if(action==null)return false;
            if(action.Id=="fit") {surface.Fit();ViewChanged();}
            else if(action.Id=="study")TogglePopup("study");
            else ToolFromKey(e,action.Id);
            e.SuppressKeyPress=true;return true;
        }
        sealed class ShortcutField : TextBox {
            public event Action Updated;
            public Keys Combination {get;private set;}
            public ShortcutField(Keys key){ReadOnly=true;ShortcutsEnabled=false;Set(key);}
            void Set(Keys key){
                Combination=key;
                Text=key==Keys.None?"—":new KeysConverter().ConvertToString(key);
                if(Updated!=null)Updated();
            }
            protected override bool ProcessCmdKey(ref Message message,Keys keyData) {
                var code=keyData&Keys.KeyCode;
                if(code==Keys.Tab||code==Keys.Escape||code==Keys.Enter)return base.ProcessCmdKey(ref message,keyData);
                if(code==Keys.Back&&keyData==Keys.Back){Set(Keys.None);return true;}
                if(code!=Keys.ControlKey&&code!=Keys.ShiftKey&&code!=Keys.Menu&&code!=Keys.LWin&&code!=Keys.RWin)
                    Set(keyData);
                return true;
            }
        }
        void ConfigureTablet() {
            var sheet=new Sheet("Графический планшет","Назначьте ту же комбинацию в драйвере планшета",465,562);
            var pressure=sheet.Put(new CheckBox{Text="Толщина от нажима Windows Ink",Checked=tablet.Pressure},22,96,415,25);
            var reverse=sheet.Put(new CheckBox{Text="Обратный конец пера включает ластик",Checked=tablet.EraserEnd},22,123,415,25);
            var fields=new Dictionary<string,ShortcutField>();
            for(int i=0;i<tabletActions.Length;i++) {
                var action=tabletActions[i];
                int key;tablet.Shortcuts.TryGetValue(action.Id,out key);
                sheet.Caption(action.Title,22,167+i*32,216);
                var field=sheet.Put(new ShortcutField((Keys)key),251,164+i*32,190,25);
                field.AccessibleName=action.Title;fields.Add(action.Id,field);
            }
            var status=sheet.Caption("Сохраняется при изменении. Backspace очищает поле.",22,455,421);
            Func<bool> save=()=>{
                var next=new TabletProfile{Pressure=pressure.Checked,EraserEnd=reverse.Checked};
                foreach(var entry in fields) {
                    int key=(int)entry.Value.Combination;
                    if(key==0)continue;
                    if(next.Shortcuts.ContainsValue(key)){status.Text="Назначьте разные сочетания клавиш.";return false;}
                    next.Shortcuts.Add(entry.Key,key);
                }
                if(recoveryAllowed)try {
                    Directory.CreateDirectory(Path.GetDirectoryName(TabletPath));
                    WriteTabletProfile(TabletPath,next);
                } catch(Exception ex) {status.Text="Не удалось сохранить: "+ex.Message;return false;}
                tablet=next;status.Text="Настройки сохранены автоматически.";return true;
            };
            foreach(var field in fields.Values)field.Updated+=()=>save();
            pressure.CheckedChanged+=(sender,args)=>save();
            reverse.CheckedChanged+=(sender,args)=>save();
            sheet.Footer("Готово",save);
            sheet.OnBoard(this,result=>{});
        }
    }
}
