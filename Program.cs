using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

static class Program {
    [STAThread]
    static int Main(string[] args) {
        if (args.Length == 1 && args[0] == "--self-test") { Verify.Run(); return 0; }
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        bool first;
        using (var instance = new Mutex(true, @"Local\PrintWorks.Desktop", out first)) {
            if (!first) { MessageBox.Show("Приложение уже открыто."); return 0; }
            try {
                string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PrintWorks", "shop.xml");
                Application.Run(new Desk(path));
                return 0;
            } catch (Exception e) {
                MessageBox.Show(e.Message, "Не удалось открыть «Цифровой цех»", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }
    }
}

partial class Desk : Form {
    static readonly Color Background = Color.FromArgb(17, 35, 38);
    static readonly Color Surface = Color.FromArgb(31, 53, 55);
    static readonly Color Muted = Color.FromArgb(178, 202, 195);
    static readonly Color Accent = Color.FromArgb(12, 111, 93);
    readonly string path;
    Shop shop;
    readonly ComboBox role = new ComboBox();
    readonly Panel body = new Panel();
    readonly Button[] nav=new Button[6]; int page, machineIndex, selectedId;
    string Role { get { return Convert.ToString(role.SelectedItem); } }

    public Desk(string path) {
        this.path = path;
        shop = Shop.Load(path); StartClock(); EnableMax();
        Text = "Цифровой цех — ремонты и ТО";
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(1280, 800);
        MinimumSize = new Size(1024, 670);
        Font = new Font("Segoe UI", 10);
        ForeColor = Color.WhiteSmoke;
        BackColor = Background;
        AutoScaleMode = AutoScaleMode.Dpi;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Padding = new Padding(20) };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(layout);
        var header=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=3,RowCount=1,Margin=Padding.Empty};
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,153));header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,153));
        layout.Controls.Add(header,0,0);
        header.Controls.Add(LabelFor("Типография",17),0,0);
        var top=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false,AutoScroll=true};
        header.Controls.Add(top,1,0);
        string[] pages={"Цех","Заявки","Станки","ТО","Сообщения","Склад"};
        for(int i=0;i<pages.Length;i++){
            int index=i;nav[i]=ButtonFor(pages[i],()=>{page=index;Render();},false);
            top.Controls.Add(nav[i]);
        }        role.Width = 133;
        role.DropDownStyle = ComboBoxStyle.DropDownList;
        role.AccessibleName = "Демонстрационная роль";
        role.Margin = new Padding(12, 10, 0, 0);
        role.Items.AddRange(new object[] { "Заявитель", "Исполнитель", "Ответственный", "Руководитель", "Кладовщик", "Закупки" });
        role.SelectedIndex = 0;
        role.SelectedIndexChanged += (s,e) => Render();
        header.Controls.Add(role,2,0);
        body.Dock = DockStyle.Fill;
        layout.Controls.Add(body, 0, 1);
        Render();
    }

    static Label LabelFor(string text, int size) {
        return new Label { Text = text, AutoSize = true, Font = new Font("Segoe UI", size, size >= 15 ? FontStyle.Bold : FontStyle.Regular), UseMnemonic = false, Margin = new Padding(3, 6, 3, 6) };
    }

    static Button ButtonFor(string text, Action action, bool primary) {
        var button = new Button { Text = text, AutoSize = true, MinimumSize = new Size(0, 36), FlatStyle = FlatStyle.Flat,
            BackColor = primary ? Accent : Surface, ForeColor = Color.White, Padding = new Padding(8, 0, 8, 0), UseMnemonic = false };
        button.FlatAppearance.BorderColor = Color.FromArgb(57, 77, 99);
        button.Click += (s, e) => action();
        return button;
    }

    static FlowLayoutPanel Column() {
        return new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown,
            WrapContents = false, AutoScroll = true, BackColor = Surface, Padding = new Padding(16) };
    }

    void Render() {
        body.SuspendLayout(); for(int i=0;i<nav.Length;i++)nav[i].BackColor=i==page?Accent:Surface;
        while (body.Controls.Count > 0) body.Controls[0].Dispose();
        if (page == 0 || page == 2) Machines();
        else if (page == 1) Tickets();
        else if(page==3) ServicePage(); else if(page==4) AlertsPage(); else StockPage();
        body.ResumeLayout();
    }

    TableLayoutPanel Split(int sidebar) {
        var split = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, sidebar));
        split.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        body.Controls.Add(split);
        return split;
    }

    bool Active(Ticket t) { return t.Status != Shop.Closed && t.Status != Shop.Canceled; }
    Color MachineColor(int i) {
        var m = shop.Machines[i];
        string state=shop.State(m); if(state=="Авария")return Color.Salmon; if(state=="Заявка на ремонт")return Color.DeepSkyBlue; if(state=="На ТО" || state=="Скоро ТО")return Color.Gold; return Color.MediumSeaGreen;
        
    }

    void Machines() {
        var split = Split(325);
        var detail = Column();
        split.Controls.Add(detail,1,0);
        var machine = shop.Machines[machineIndex];
        if (page == 0) {
            var map = new Floor { Dock = DockStyle.Fill, Current = machineIndex, StatusColor = MachineColor };
            map.Selected += i => { machineIndex = i; Render(); };
            var left = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, BackColor = Background };
            left.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
            left.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            left.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
                        var overview=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false,AutoScroll=true};
            overview.Controls.Add(LabelFor("Карта цеха",20));
            var alarms=shop.Tickets.Where(t=>Active(t)&&t.Priority=="Авария").OrderByDescending(t=>t.Id).ToList();
            var count=LabelFor("Аварии: "+alarms.Count+"     В работе и ожидании: "+shop.Tickets.Count(t=>Active(t)&&t.Status!=Shop.New),11);
            count.Margin=new Padding(16,16,0,0);count.ForeColor=alarms.Count==0?Muted:Color.LightSalmon;overview.Controls.Add(count);
            left.Controls.Add(overview,0,0);
            if(alarms.Count>0){
                detail.Controls.Add(LabelFor("Аварии в цехах",15));
                foreach(var alarm in alarms){
                    var t=alarm;var m=shop.Machines.First(x=>x.Number==t.Machine);
                    detail.Controls.Add(ButtonFor("№"+t.Id+" · "+m.Area,()=>{selectedId=t.Id;page=1;Render();},true));
                }
            }
            left.Controls.Add(map, 0, 1);
            var legend = new FlowLayoutPanel { Dock=DockStyle.Fill, AutoScroll=true }; string[] names={"Работает","Ремонт","ТО","Авария"}; Color[] colors={Color.MediumSeaGreen,Color.DeepSkyBlue,Color.Gold,Color.Salmon}; for(int i=0;i<names.Length;i++){var tag=LabelFor("● "+names[i],10); tag.ForeColor=colors[i]; legend.Controls.Add(tag);}
            legend.ForeColor = Muted;
            left.Controls.Add(legend, 0, 2);
            split.Controls.Add(left,0,0);
        } else {
            var list = Column();
            list.Controls.Add(LabelFor("Оборудование", 21));
            for (int i = 0; i < shop.Machines.Count; i++) {
                int index = i;
                list.Controls.Add(ButtonFor(shop.Machines[i].ToString(), () => { machineIndex = index; Render(); }, i == machineIndex));
            }
            split.Controls.Add(list,0,0);
        }
        var picker = new ComboBox { Width = 276, DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "Выбрать оборудование" };
        picker.Items.AddRange(shop.Machines.Cast<object>().ToArray());
        picker.SelectedIndex = machineIndex;
        picker.SelectedIndexChanged += (s,e) => { machineIndex = picker.SelectedIndex; Render(); };
        detail.Controls.Add(picker);
        detail.Controls.Add(LabelFor(machine.Name, 16));
        detail.Controls.Add(LabelFor(machine.Area + " · Инв. № " + machine.Number, 10));
        detail.Controls.Add(LabelFor("Следующее ТО  " + Shop.Due(machine), 11));
        var state = LabelFor(shop.Tickets.Any(t => t.Machine == machine.Number && Active(t)) ? "Есть открытая заявка" :
            machine.NextService <= DateTime.Today.AddDays(7) ? "ТО в ближайшие 7 дней" : "Без открытых заявок", 10);
        state.ForeColor = MachineColor(machineIndex);
        state.Text=shop.State(machine); detail.Controls.Add(state);
        if (Role == "Заявитель") detail.Controls.Add(ButtonFor("Создать заявку", () => Create(machine), true));
        detail.Controls.Add(LabelFor("Заявки по оборудованию", 14));
        foreach (var t in shop.Tickets.Where(t => t.Machine == machine.Number).OrderByDescending(x => x.Id)) {
            var ticket = t;
            detail.Controls.Add(ButtonFor("№" + t.Id + " · " + t.Kind + " · " + t.Status,
                () => { selectedId = ticket.Id; page = 1; Render(); }, false));
        }
    }

    void Schedule() {
        var column = Column();
        body.Controls.Add(column);
        column.Controls.Add(LabelFor("График планового ТО", 21));
        foreach (var m in shop.Machines.OrderBy(x => x.NextService)) {
            var line = LabelFor(m.NextService.ToString("dd.MM.yyyy") + "      " + m + "      " + m.Area, 12);
            if (m.NextService.Date < DateTime.Today) line.ForeColor = Color.Salmon;
            column.Controls.Add(line);
        }
    }

}
