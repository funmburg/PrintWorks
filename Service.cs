using System;
using System.Linq;
using System.Collections.Generic;
using System.Windows.Forms;
using System.Drawing;

public class Alert {
    public string Key, Audience, Message, Machine;
    public int TicketId;
    public DateTime Created;
    public bool Read;
}
public partial class Shop {
    public const string Days="Дни", Months="Месяцы", HoursBasis="Моточасы", Cycles="Циклы";
    public List<Alert> Alerts=new List<Alert>();
    public static bool ActiveTicket(Ticket t) { return t.Status!=Closed && t.Status!=Canceled; }
    public static bool Meter(Machine m) { return m.Basis==HoursBasis || m.Basis==Cycles; }
    public static long Reading(Machine m) { return m.Basis==Cycles ? m.CycleCount : m.HourCount; }
    public static string Due(Machine m) { return Meter(m) ? m.Threshold+" "+m.Basis.ToLowerInvariant() : m.NextService.ToString("dd.MM.yyyy"); }
    bool AlertOnce(string key,string role,string message,Machine m,Ticket t,DateTime now) {
        if(Alerts.Any(a=>a.Key==key && a.Audience==role)) return false;
        Alerts.Add(new Alert{Key=key,Audience=role,Message=message,Machine=m.Number,TicketId=t==null?0:t.Id,Created=now});return true;
    }
    public bool Evaluate(DateTime now) {
        bool changed=PrepareStock();
        foreach(var m in Machines) {
            var open=Tickets.FirstOrDefault(t=>t.Scheduled && t.Machine==m.Number && ActiveTicket(t));
            long left=Meter(m)?m.Threshold-Reading(m):(m.NextService.Date-now.Date).Days;
            string key=m.Number+"/"+m.Basis+"/"+(Meter(m)?m.Threshold.ToString():m.NextService.ToString("yyyyMMdd"));
            if(left<=0 && open==null) {
                open=Add(m,m.ServiceWork,"Планово","Плановое ТО","Система");open.Scheduled=true;
                changed=true;
                AlertOnce("due/"+key,"Ответственный","Создана заявка на ТО №"+open.Id,m,open,now);
            }
            if(left<0) changed|=AlertOnce("late/"+key,"Руководитель","Просрочено ТО · "+m.Name,m,open,now);
            if(!Meter(m) && left>0 && left<=7 && open==null)
                changed|=AlertOnce("soon/"+key+"/"+(left==1?"1":"7"),"Ответственный","ТО "+Due(m)+" · "+m.Name,m,null,now);
        }
        return changed;
    }
    public void Move(Ticket t,string to,string role,string note) {
        if((to==Review || to==Canceled || to==New) && Requirements.Any(l=>l.TicketId==t.Id && (to!=Review || l.Issued!=l.Requested)))throw new InvalidOperationException("Заявка связана с запчастями. Для приёмки завершите выдачу всех позиций."); Change(t,to,role,note);
        var m=Machines.Single(x=>x.Number==t.Machine);
        AlertOnce("status/"+t.Id+"/"+t.Log.Count,t.Scheduled?"Ответственный":"Заявитель","Заявка №"+t.Id+" · "+to,m,t,DateTime.Now);
        if(to==Review)AlertOnce("review/"+t.Id+"/"+t.Log.Count,"Ответственный","Требуется приёмка №"+t.Id,m,t,DateTime.Now);
        if(t.Scheduled && to==Closed) {
            m.LastService=t.Closed;
            if(Meter(m))m.Threshold=checked(Reading(m)+m.Interval);
            else m.NextService=m.Basis==Months?t.Closed.Date.AddMonths(m.Interval):t.Closed.Date.AddDays(m.Interval);
        }
    }
    public string State(Machine m) {
        var active=Tickets.Where(t=>ActiveTicket(t)&&t.Machine==m.Number);
        if(active.Any(t=>t.Priority=="Авария"))return "Авария";
        if(active.Any(t=>t.Kind=="Ремонт"))return "Заявка на ремонт";
        if(active.Any())return "На ТО";
        return !Meter(m)&&m.NextService.Date<=DateTime.Today.AddDays(7)?"Скоро ТО":"Работает";
    }
}
partial class Desk {
    readonly System.Windows.Forms.Timer poll=new System.Windows.Forms.Timer();
    void StartClock() {
        if((shop.Evaluate(DateTime.Now) | shop.PollBalances()) || shop.Revision==0)shop.Save(path);
        poll.Interval=60000;
        poll.Tick+=(s,e)=>{if(Enabled && (shop.Evaluate(DateTime.Now) | shop.PollBalances()))Commit(()=>{});};
        poll.Start();FormClosed+=(s,e)=>poll.Dispose();
    }
    void AlertsPage() {
        var col=Column();body.Controls.Add(col);col.Controls.Add(LabelFor("Уведомления · "+Role,20));
        var items=shop.Alerts.Where(a=>a.Audience==Role).OrderByDescending(a=>a.Created).ToList();
        if(items.Count==0)col.Controls.Add(LabelFor("Уведомлений пока нет",11));
        foreach(var entry in items) {
            var a=entry;
            col.Controls.Add(ButtonFor((a.Read?"":"●  ")+a.Created.ToString("dd.MM HH:mm")+"   "+a.Message,()=>Commit(()=>{
                a.Read=true;
                if(a.TicketId>0){selectedId=a.TicketId;page=1;}else{machineIndex=shop.Machines.FindIndex(m=>m.Number==a.Machine);page=0;}
            }),!a.Read));
        }
    }
    void ServicePage() {
        var col=Column();body.Controls.Add(col);col.Controls.Add(LabelFor("График планового ТО",21));
        foreach(var item in shop.Machines.OrderBy(x=>x.NextService)) {
            var m=item;col.Controls.Add(LabelFor(m.Name+" · "+m.Number,14));
            col.Controls.Add(LabelFor(m.ServiceWork+"   ·   Срок "+Shop.Due(m)+"   ·   Интервал "+m.Interval+" ("+m.Basis+")",11));
            var open=shop.Tickets.FirstOrDefault(t=>t.Scheduled && t.Machine==m.Number && Shop.ActiveTicket(t));
            if(open!=null)col.Controls.Add(ButtonFor("Открыть ТО №"+open.Id,()=>{selectedId=open.Id;page=1;Render();},true));
            if(Role=="Ответственный"){
                if(open==null)col.Controls.Add(ButtonFor("Изменить график",()=>EditService(m),false));
                col.Controls.Add(ButtonFor("Внести показания",()=>ReadMeters(m),false));
            }
        }
    }
    void ReadMeters(Machine m) {
        using(var f=Dialog("Показания · "+m.Number)){
            var c=(FlowLayoutPanel)f.Controls[0];var h=Field(c,"Моточасы",false,m.HourCount.ToString());var cycles=Field(c,"Циклы",false,m.CycleCount.ToString());
            long hours=0,count=0;
            Submit(f,c,"Сохранить",()=>long.TryParse(h.Text,out hours)&&long.TryParse(cycles.Text,out count)&&hours>=m.HourCount&&count>=m.CycleCount&&hours<=1000000000000L&&count<=1000000000000L,"Введите неубывающие показания.");
            if(f.ShowDialog(this)==DialogResult.OK)Commit(()=>{m.HourCount=hours;m.CycleCount=count;});
        }
    }
    void EditService(Machine m) {
        using(var f=Dialog("График ТО · "+m.Number)){
            var c=(FlowLayoutPanel)f.Controls[0];var work=Field(c,"Регламентные работы",false,m.ServiceWork);
            var basis=Select(c,"Периодичность",new object[]{Shop.Days,Shop.Months,Shop.HoursBasis,Shop.Cycles});basis.SelectedItem=m.Basis;
            var interval=Field(c,"Интервал",false,m.Interval.ToString());
            c.Controls.Add(LabelFor("Следующая дата",10));var d=new DateTimePicker{Value=m.NextService,Format=DateTimePickerFormat.Short};c.Controls.Add(d);
            var threshold=Field(c,"Порог наработки",false,m.Threshold.ToString());
            Action toggle=()=>{d.Enabled=basis.Text==Shop.Days||basis.Text==Shop.Months;threshold.Enabled=!d.Enabled;};
            basis.SelectedIndexChanged+=(s,e)=>toggle();toggle();int n=0;long limit=0;
            Submit(f,c,"Сохранить",()=>work.Text.Trim().Length>0&&int.TryParse(interval.Text,out n)&&n>0&&n<=(basis.Text==Shop.Months?1200:100000)&&
                (!threshold.Enabled||(long.TryParse(threshold.Text,out limit)&&limit>0&&limit<=1000000000000L)),"Проверьте название, интервал и порог.");
            if(f.ShowDialog(this)==DialogResult.OK)Commit(()=>{m.ServiceWork=work.Text.Trim();m.Basis=basis.Text;m.Interval=n;m.NextService=d.Value.Date;if(threshold.Enabled)m.Threshold=limit;});
        }
    }
}
