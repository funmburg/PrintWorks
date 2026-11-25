using System;
using System.IO;
using System.Windows.Forms;

static class Verify {
    public static void Run() {
        string file = Path.Combine(Environment.CurrentDirectory, "PrintWorks-test-" + Guid.NewGuid() + ".xml");

        var s = Shop.Load(file);
        try {
            var bot=Shop.Load(file);
            bot.Answer(10,"1","/new");bot.Answer(10,"2","P-101");bot.Answer(10,"3","1");
            int initial=bot.Tickets.Count;
            string confirmation=bot.Answer(10,"4","Застряла бумага");bot.Save(file);bot=Shop.Load(file);
            if(bot.Answer(10,"4","Застряла бумага")!=confirmation || bot.Tickets.Count!=initial+1 || bot.Tickets[initial].Priority!="Авария")throw new Exception("MAX duplicate or wrong priority");
            if(!bot.Answer(10,"5","/my").Contains("№"+bot.Tickets[initial].Id) || bot.Answer(11,"6","/my").Contains("№"))throw new Exception("MAX ownership");
            var group=Shop.Load(file);
            int before=group.Tickets.Count;
            if(group.Answer(10,-123,"a","обычный разговор",null)!=null)throw new Exception("Group noise");
            group.Answer(10,-123,"b","/new",null);group.DialogFor(10,-123).Prompt="q1";
            group.Answer(11,-123,"c","/new",null);group.DialogFor(11,-123).Prompt="other";
            group.Answer(10,"priv","/new");
            if(group.Answer(11,-123,"d","P-101","q1")!=null || group.Answer(10,-124,"e","P-101","q1")!=null)throw new Exception("Crossed dialogs");
            group.Answer(10,-123,"f","P-101","q1");group.DialogFor(10,-123).Prompt="q2";
            group.Answer(10,-123,"g","1","q2");group.DialogFor(10,-123).Prompt="q3";
            if(group.Answer(10,-123,"h","обед в два",null)!=null)throw new Exception("Captured unrelated message");
            string ok=group.Answer(10,-123,"i","Станок остановился","q3");
            group.Save(file);group=Shop.Load(file);
            if(!ok.Contains("№") || group.Answer(10,-123,"i","Станок остановился","q3")!=ok || group.Tickets.Count!=before+1 || group.DialogFor(10,0).Step!="machine" || group.DialogFor(11,-123).Step!="machine")throw new Exception("Group flow or duplicate");
            var demo = Shop.Load(file);
            var m = demo.Machines[0]; var now = DateTime.Now;
            m.NextService = now.Date;
            demo.Evaluate(now); demo.Evaluate(now);
            var planned = demo.Tickets.FindAll(x => x.Scheduled && x.Machine==m.Number);
            if(planned.Count!=1) throw new Exception("Duplicate scheduled ticket");
            var task=planned[0]; demo.Move(task, Shop.Accepted,"Исполнитель",null);
            demo.Move(task,Shop.Repair,"Исполнитель",null);task.Cause="Регламент";task.Work="Выполнено";task.Hours=1;
            demo.Move(task,Shop.Review,"Исполнитель",null);demo.Move(task,Shop.Closed,"Ответственный",null);
            if(m.NextService!=task.Closed.Date.AddDays(m.Interval))throw new Exception("Next service date");
            var repair=demo.Add(m,"Подача листа","Авария","Ремонт","Заявитель");
            demo.Move(repair,Shop.Accepted,"Исполнитель",null);
            demo.Request(repair,demo.Stock[1],3);demo.Request(repair,demo.Stock[2],2);
            foreach(var line in demo.Requirements)line.Expected=now.Date.AddDays(2);
            demo.WaitParts(repair,"Поставка");
            demo.Receive(demo.Stock[1],2);demo.Receive(demo.Stock[2],1);
            if(repair.Status!=Shop.Waiting)throw new Exception("Partial receipt resumed repair");
            demo.Receive(demo.Stock[2],1);
            if(repair.Status!=Shop.Repair)throw new Exception("All parts did not resume repair");
            foreach(var line in demo.Requirements)demo.Issue(line);
            if(demo.Free(demo.Stock[2])!=0)throw new Exception("Bad inventory");
            demo.Save(file); var reload=Shop.Load(file);
            if(reload.Requirements.Count!=2 || reload.Alerts.Count!=demo.Alerts.Count)throw new Exception("Persistence");
            var t = s.Add(s.Machines[1], "Подача бумаги", "Срочно", "Ремонт", "Заявитель");
            Shop.Change(t, Shop.Accepted, "Исполнитель", null);
            Shop.Change(t, Shop.Diagnosis, "Исполнитель", null);
            Shop.Change(t, Shop.Repair, "Исполнитель", null);
            bool blocked = false;
            try { Shop.Change(t, Shop.Review, "Исполнитель", null); }
            catch (InvalidOperationException) { blocked = true; }
            if (!blocked || t.Status != Shop.Repair) throw new Exception("Приёмка без результата работ");
            t.Cause = "Износ ролика";
            t.Work = "Заменили ролик";
            t.Parts = "Ролик подачи";
            t.Hours = 1.5m;
            Shop.Change(t, Shop.Review, "Исполнитель", null);
            Shop.Change(t, Shop.Repair, "Ответственный", "Проверить подачу");
            Shop.Change(t, Shop.Review, "Исполнитель", null);
            s.Save(file);
            Shop.Change(t, Shop.Closed, "Ответственный", null);
            s.Save(file);
            var saved = Shop.Load(file).Tickets.Find(x => x.Id == t.Id);
            if (saved.Status != Shop.Closed || saved.Log.Count != 8 || saved.Hours != 1.5m)
                throw new Exception("Заявка или журнал не восстановились");
            if (Environment.UserInteractive) {
                using (var desk = new Desk(file)) {
                    desk.Show();
                    Application.DoEvents();
                    desk.Close();
                }
            }
        } finally {
            if (File.Exists(file)) File.Delete(file);
            if (File.Exists(file + ".alternate")) File.Delete(file + ".alternate");
        }
    }
}
