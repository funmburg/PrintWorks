using System;
using System.Linq;
using System.Collections.Generic;
using System.Windows.Forms;

public class Spare {
    public string Code,Name;
    public int OnHand,PreviousBalance; public bool HasSnapshot;
    public override string ToString(){return Code+" · "+Name;}
}
public class PartLine {
    public int TicketId,Requested,Reserved,Issued;
    public string Code;
    public DateTime Expected;
    public int Missing {get{return Requested-Reserved-Issued;}}
}
public partial class Shop {
    public const string Waiting="Ожидание запчастей";
    public List<Spare> Stock=new List<Spare>();
    public List<PartLine> Requirements=new List<PartLine>();
    public bool StockInitialized;
    public bool PrepareStock(){
        if(StockInitialized)return false;
        Stock.Add(new Spare{Code="PR-01",Name="Ролик подачи",OnHand=4});
        Stock.Add(new Spare{Code="PR-02",Name="Ремень привода",OnHand=1});
        Stock.Add(new Spare{Code="PR-03",Name="Датчик листа",OnHand=0});
        StockInitialized=true;return true;
    }
    public int Free(Spare p){return p.OnHand-Requirements.Where(l=>l.Code==p.Code).Sum(l=>l.Reserved);}
    public void Request(Ticket t,Spare p,int n){
        if(t.Status!=Accepted && t.Status!=Diagnosis && t.Status!=Repair)throw new InvalidOperationException("Возьмите заявку в работу.");
        if(n<1 || n>1000000)throw new InvalidOperationException("Проверьте количество.");
        var line=new PartLine{TicketId=t.Id,Code=p.Code,Requested=n,Reserved=Math.Min(n,Free(p))};
        Requirements.Add(line);Record(t,"Исполнитель","Запрошено "+p.Name+" × "+n+", к закупке "+line.Missing);
    }
    public void WaitParts(Ticket t,string reason){
        var lines=Requirements.Where(l=>l.TicketId==t.Id && l.Missing>0).ToList();
        if(!new[]{Accepted,Diagnosis,Repair}.Contains(t.Status)||lines.Count==0||lines.Any(l=>l.Expected==DateTime.MinValue)||String.IsNullOrWhiteSpace(reason))
            throw new InvalidOperationException("Нужны причина и даты поставки всех недостающих позиций.");
        Record(t,"Исполнитель",t.Status+" → "+Waiting+" · "+reason+" · до "+lines.Max(l=>l.Expected).ToString("dd.MM.yyyy"));
        t.Status=Waiting;
    }
    public void Receive(Spare p,int n){
        if(n<1)throw new InvalidOperationException("Введите положительное количество.");
        p.OnHand=checked(p.OnHand+n);
        foreach(var l in Requirements.Where(x=>x.Code==p.Code && x.Missing>0)){
            int reserve=Math.Min(l.Missing,Free(p));if(reserve==0)break;l.Reserved+=reserve;
            var t=Tickets.Single(x=>x.Id==l.TicketId);
            if(t.Status==Waiting && !Requirements.Any(x=>x.TicketId==t.Id&&x.Missing>0)){
                t.Status=Repair;Record(t,"Система",Waiting+" → "+Repair+" · комплект зарезервирован");
                AlertOnce("parts/"+t.Id+"/"+t.Log.Count,"Исполнитель","Детали поступили · заявка №"+t.Id,Machines.Single(m=>m.Number==t.Machine),t,DateTime.Now);
            }
        }
    }
    public void Issue(PartLine l){
        if(l.Reserved==0)throw new InvalidOperationException("Нет резерва для выдачи.");
        var p=Stock.Single(v=>v.Code==l.Code);p.OnHand-=l.Reserved;l.Issued+=l.Reserved;
        Record(Tickets.Single(t=>t.Id==l.TicketId),"Кладовщик","Выдано "+p.Name+" × "+l.Reserved);l.Reserved=0;
    }
}
partial class Desk {
    void StockPage(){
        var col=Column();body.Controls.Add(col);col.Controls.Add(LabelFor("Склад и закупки",20));
        BalanceControls(col); foreach(var item in shop.Stock){
            var p=item;
            col.Controls.Add(LabelFor(p+"   ·   на складе "+p.OnHand+"   ·   свободно "+shop.Free(p),12));
            if(Role=="Кладовщик")col.Controls.Add(ButtonFor("Поступление · "+p.Code,()=>{
                string s=Ask("Поступление","Количество");if(s==null)return;int n;
                if(!int.TryParse(s,out n)||n<1){MessageBox.Show(this,"Введите целое положительное число.");return;}
                Commit(()=>shop.Receive(p,n));
            },false));
        }
        col.Controls.Add(LabelFor("Потребности по заявкам",15));
        if(shop.Requirements.Count==0)col.Controls.Add(LabelFor("Запчасти пока не запрашивали",11));
        foreach(var item in shop.Requirements.AsEnumerable().Reverse()){
            var l=item;col.Controls.Add(ButtonFor("№"+l.TicketId+" · "+l.Code+" · купить "+l.Missing+" · к выдаче "+l.Reserved+" · выдано "+l.Issued,()=>{page=1;selectedId=l.TicketId;Render();},false));
            if(l.Expected!=DateTime.MinValue)col.Controls.Add(LabelFor("Поставка "+l.Expected.ToString("dd.MM.yyyy"),10));
            if(Role=="Кладовщик"&&l.Reserved>0)col.Controls.Add(ButtonFor("Подтвердить выдачу",()=>Commit(()=>shop.Issue(l)),true));
            if(Role=="Закупки"&&l.Missing>0)col.Controls.Add(ButtonFor("Дата поставки",()=>{
                using(var f=Dialog("Ожидаемая поставка")){
                    var c=(FlowLayoutPanel)f.Controls[0];var d=new DateTimePicker{MinDate=DateTime.Today};c.Controls.Add(d);
                    Submit(f,c,"Сохранить",()=>true,"");
                    if(f.ShowDialog(this)==DialogResult.OK)Commit(()=>{l.Expected=d.Value.Date;Shop.Record(shop.Tickets.Single(x=>x.Id==l.TicketId),Role,l.Code+" · поставка "+l.Expected.ToString("dd.MM.yyyy"));});
                }
            },false));
        }
    }
    void TicketStock(FlowLayoutPanel col,Ticket t){
        foreach(var l in shop.Requirements.Where(x=>x.TicketId==t.Id))col.Controls.Add(LabelFor(l.Code+" × "+l.Requested+" · к закупке "+l.Missing+" · выдано "+l.Issued,11));
        if(Role!="Исполнитель" || !new[]{Shop.Accepted,Shop.Diagnosis,Shop.Repair}.Contains(t.Status))return;
        col.Controls.Add(ButtonFor("Запросить запчасть",()=>{
            using(var f=Dialog("Запчасти · №"+t.Id)){
                var c=(FlowLayoutPanel)f.Controls[0]; var p=Select(c,"Деталь",shop.Stock.Cast<object>().ToArray());
                var amount=Field(c,"Количество",false,"1");int n=0;
                Submit(f,c,"Запросить",()=>int.TryParse(amount.Text,out n)&&n>0&&n<=1000000,"Введите целое количество от 1 до 1 000 000.");
                if(f.ShowDialog(this)==DialogResult.OK)Commit(()=>shop.Request(t,(Spare)p.SelectedItem,n));
            }
        },false));
        if(shop.Requirements.Any(x=>x.TicketId==t.Id && x.Missing>0))col.Controls.Add(ButtonFor("Ожидание запчастей",()=>{
            string reason=Ask("Ожидание","Причина ожидания");if(reason!=null)Commit(()=>shop.WaitParts(t,reason));
        },false));
    }
}
