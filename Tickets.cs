using System;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

partial class Desk {
    void Tickets() {
        var split = Split(590);
        var list = Column();
        var details = Column();
        split.Controls.Add(list, 0, 0);
        split.Controls.Add(details, 1, 0);
        list.Controls.Add(LabelFor("Заявки на ремонт и ТО", 19));
        list.Controls.Add(LabelFor("Открыто: " + shop.Tickets.Count(Active) + "     Закрыто: " + shop.Tickets.Count(t => t.Status == Shop.Closed), 10));
        if (Role == "Заявитель") list.Controls.Add(ButtonFor("Новая заявка", () => Create(shop.Machines[machineIndex]), true));
        var rows = new ListBox { Width = 350, Height = 355, BackColor = Surface, ForeColor = Color.WhiteSmoke, AccessibleName = "Список заявок" };
        foreach (var t in shop.Tickets.OrderByDescending(x => x.Id)) rows.Items.Add(t);
        var selected = shop.Tickets.FirstOrDefault(t => t.Id == selectedId);
        if (selected != null) rows.SelectedItem = selected;
        rows.SelectedIndexChanged += (s, e) => {
            var ticket = rows.SelectedItem as Ticket;
            if (ticket == null) return;
            selectedId = ticket.Id;
            while (details.Controls.Count > 0) details.Controls[0].Dispose();
            TicketDetail(details, ticket);
        };
        list.Controls.Add(rows);
        if (selected != null) TicketDetail(details, selected);
        else details.Controls.Add(LabelFor("Выберите заявку", 16));
    }

    void TicketDetail(FlowLayoutPanel details, Ticket t) {
        var machine = shop.Machines.Single(m => m.Number == t.Machine);
        details.Controls.Add(LabelFor("Заявка №" + t.Id + " · " + machine.Name, 17));
        details.Controls.Add(LabelFor(t.Kind + "     " + t.Priority + "     " + t.Status, 11));
        details.Controls.Add(new Label { Text = t.Description, AutoSize = true, MaximumSize = new Size(510, 0), UseMnemonic = false });
        if (!String.IsNullOrEmpty(t.Work)) {
            details.Controls.Add(new Label { Text = "Причина: " + t.Cause + "\nРаботы: " + t.Work + "\nДетали: " + t.Parts + "\nТрудозатраты: " + t.Hours + " ч",
                AutoSize = true, MaximumSize = new Size(510, 0), UseMnemonic = false });
        }
        string[] actions = Shop.Actions(t, Role);
        var buttons = new FlowLayoutPanel { AutoSize = true, MaximumSize = new Size(524, 0) };
        foreach (string action in actions) {
            string target = action;
            string caption = target == Shop.Accepted ? "Взять в работу" :
                target == Shop.Closed ? "Принять работу" :
                target == Shop.Review ? "Передать на приёмку" :
                target == Shop.Repair && t.Status == Shop.Review ? "Вернуть в ремонт" :
                target == Shop.New ? "В общий список" : target == Shop.Canceled ? "Отменить" : target;
            buttons.Controls.Add(ButtonFor(caption, () => Transition(t, target), true));
        }
        details.Controls.Add(buttons);
        if (Active(t)) details.Controls.Add(ButtonFor("Добавить комментарий", () => {
            string message = Ask("Комментарий", "Текст комментария");
            if (!String.IsNullOrWhiteSpace(message)) Commit(() => Shop.Record(t, Role, message.Trim()));
        }, false));
        TicketStock(details,t); details.Controls.Add(LabelFor("Журнал", 14));
        details.Controls.Add(new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
            Width = 513, Height = 165, AccessibleName = "Журнал заявки", Lines = t.Log.AsEnumerable().Reverse().ToArray() });
    }

    void Create(Machine selected) {
        using (var form = Dialog("Новая заявка")) {
            var layout = (FlowLayoutPanel)form.Controls[0];
            var equipment = Select(layout, "Оборудование", shop.Machines.Cast<object>().ToArray());
            equipment.SelectedItem = selected;
            var kind = Select(layout, "Вид", new object[] { "Ремонт", "Плановое ТО" });
            var priority = Select(layout, "Срочность", new object[] { "Срочно", "Авария", "Планово" });
            var description = Field(layout, "Описание", true, "");
            Submit(form, layout, "Создать", () => !String.IsNullOrWhiteSpace(description.Text), "Введите описание.");
            if (form.ShowDialog(this) != DialogResult.OK) return;
            Commit(() => {
                var t = shop.Add((Machine)equipment.SelectedItem, description.Text, priority.Text, kind.Text, Role);
                selectedId = t.Id;
                page = 1;
            });
        }
    }

    void Transition(Ticket ticket, string target) {
        if (target == Shop.Review) {
            using (var form = Dialog("Результат работ")) {
                var layout = (FlowLayoutPanel)form.Controls[0];
                var cause = Field(layout, "Причина отказа или основание для ТО", false, ticket.Cause);
                var work = Field(layout, "Выполненные работы", true, ticket.Work);
                var parts = Field(layout, "Заменённые детали", false, ticket.Parts);
                var effort = Field(layout, "Трудозатраты, часы", false, ticket.Hours > 0 ? ticket.Hours.ToString() : "");
                decimal hours = 0;
                Submit(form, layout, "Передать", () => !String.IsNullOrWhiteSpace(cause.Text) &&
                    !String.IsNullOrWhiteSpace(work.Text) && Decimal.TryParse(effort.Text, NumberStyles.AllowDecimalPoint, CultureInfo.CurrentCulture, out hours) && hours > 0,
                    "Введите причину, выполненные работы и число часов больше нуля.");
                if (form.ShowDialog(this) != DialogResult.OK) return;
                Commit(() => {
                    ticket.Cause = cause.Text.Trim(); ticket.Work = work.Text.Trim();
                    ticket.Parts = parts.Text.Trim(); ticket.Hours = hours;
                    shop.Move(ticket, target, Role, null);
                });
            }
            return;
        }
        string note = null;
        if (ticket.Status == Shop.Review && target == Shop.Repair) {
            note = Ask("Возврат в ремонт", "Причина возврата");
            if (String.IsNullOrWhiteSpace(note)) return;
        }
        Commit(() => shop.Move(ticket, target, Role, note));
    }

    void Commit(Action action) {
        try { action(); shop.Evaluate(DateTime.Now); shop.Save(path); }
        catch (Exception e) {
            MessageBox.Show(this, e.Message, "Изменение не сохранено", MessageBoxButtons.OK, MessageBoxIcon.Error);
            shop = Shop.Load(path);
        }
        Render();
    }

    string Ask(string title, string prompt) {
        using (var form = Dialog(title)) {
            var layout = (FlowLayoutPanel)form.Controls[0];
            var field = Field(layout, prompt, true, "");
            Submit(form, layout, "Сохранить", () => !String.IsNullOrWhiteSpace(field.Text), "Введите текст.");
            return form.ShowDialog(this) == DialogResult.OK ? field.Text : null;
        }
    }

    Form Dialog(string title) {
        var form = new Form { Text = title, ClientSize = new Size(475, 480), StartPosition = FormStartPosition.CenterParent,
            Font = Font, BackColor = Surface, ForeColor = ForeColor, FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false, MinimizeBox = false, ShowInTaskbar = false };
        form.Controls.Add(Column());
        return form;
    }

    static ComboBox Select(FlowLayoutPanel layout, string title, object[] choices) {
        layout.Controls.Add(LabelFor(title, 10));
        var input = new ComboBox { Width = 390, DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = title };
        input.Items.AddRange(choices);
        input.SelectedIndex = 0;
        layout.Controls.Add(input);
        return input;
    }

    static TextBox Field(FlowLayoutPanel layout, string title, bool multiline, string value) {
        layout.Controls.Add(LabelFor(title, 10));
        var input = new TextBox { Width = 390, Height = multiline ? 75 : 28, Multiline = multiline, Text = value ?? "", AccessibleName = title };
        layout.Controls.Add(input);
        return input;
    }

    static void Submit(Form form, FlowLayoutPanel layout, string label, Func<bool> valid, string error) {
        var buttons = new FlowLayoutPanel { AutoSize = true };
        var submit = ButtonFor(label, () => {
            if (valid()) form.DialogResult = DialogResult.OK;
            else MessageBox.Show(form, error);
        }, true);
        var cancel = ButtonFor("Отмена", () => form.DialogResult = DialogResult.Cancel, false);
        buttons.Controls.Add(submit);
        buttons.Controls.Add(cancel);
        layout.Controls.Add(buttons);
        form.AcceptButton = submit;
        form.CancelButton = cancel;
    }
}
