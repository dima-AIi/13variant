using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FurnitureWorkshop.Core;
using Microsoft.Win32;

namespace FurnitureWorkshop.Desktop;

internal sealed class WorkshopDialogs(Window owner)
{
    private static readonly CultureInfo Russian = CultureInfo.GetCultureInfo("ru-RU");

    public OrderInput? Order(OrderInfo? order, CustomerInfo[] customers, UserInfo[] assemblers)
    {
        var form = new Form(owner, order is null ? "Новый заказ" : $"Заказ №{order.Id}");
        var customer = new ComboBox { ItemsSource = customers, DisplayMemberPath = "Name", SelectedValuePath = "Id" };
        customer.SelectedValue = order?.CustomerId ?? customers[0].Id;
        form.Add("_Заказчик", customer);

        var title = form.Text("_Изделие", order?.Title, 180);
        var address = form.Text("_Адрес установки", order?.Address, 220);
        var notes = form.Text("_Примечания и размеры", order?.Notes, 2000, multiline: true);

        var assignees = new[] { new UserInfo(0, "", "Без назначения", "") }.Concat(assemblers).ToArray();
        var assembler = new ComboBox { ItemsSource = assignees, DisplayMemberPath = "DisplayName", SelectedValuePath = "Id" };
        assembler.SelectedValue = order?.AssemblerId ?? 0;
        form.Add("_Сборщик", assembler);

        var amount = form.Text("С_умма, ₽", order?.Amount?.ToString("0.##", Russian), 24);
        var delivery = new DatePicker { SelectedDate = order?.DeliveryDate?.ToDateTime(TimeOnly.MinValue) };
        DatePickerDateValidationErrorEventArgs? invalidDate = null;
        delivery.DateValidationError += (_, error) => invalidDate = error;
        form.Add("_Дата доставки", delivery);

        return form.Save("Сохранить", () =>
        {
            if (customer.SelectedValue is not int customerId)
                throw new RuleException("Выберите заказчика.");

            decimal? actualAmount = null;
            if (amount.Text.Trim().Length > 0)
            {
                var amountText = amount.Text.Trim().Replace('.', ',');
                if (!decimal.TryParse(amountText, NumberStyles.AllowDecimalPoint, Russian, out var parsed) || parsed <= 0)
                    throw new RuleException("Введите положительную сумму, например 18500,50.");
                actualAmount = parsed;
            }

            if (invalidDate is not null && delivery.SelectedDate is null && !string.IsNullOrWhiteSpace(delivery.Text))
                throw new RuleException("Проверьте дату доставки.");

            int? assemblerId = assembler.SelectedValue is int id && id > 0 ? id : null;
            var deliveryDate = delivery.SelectedDate is { } day ? DateOnly.FromDateTime(day) : (DateOnly?)null;
            return new OrderInput(customerId, InputRules.Required(title.Text, "Изделие", 180),
                InputRules.Required(address.Text, "Адрес установки", 220), notes.Text, assemblerId,
                actualAmount, deliveryDate, order?.Version);
        });
    }

    public CustomerInput? Customer(CustomerInfo? customer)
    {
        var form = new Form(owner, customer is null ? "Новый заказчик" : "Редактирование заказчика");
        var name = form.Text("_Имя", customer?.Name, 150);
        var phone = form.Text("_Телефон", customer?.Phone, 40);
        var email = form.Text("_Эл. почта", customer?.Email, 150);
        return form.Save("Сохранить", () => InputRules.Customer(new CustomerInput(name.Text, phone.Text, email.Text)));
    }

    public TransitionInput? Transition(OrderInfo order)
    {
        var form = new Form(owner, $"Смена статуса · заказ №{order.Id}");
        form.Summary($"Сейчас: {WorkshopText.Status(order.Status)}");
        var options = OrderRules.Next(order.Status).Select(status => new StatusOption(status, WorkshopText.Status(status))).ToArray();
        var status = new ComboBox { ItemsSource = options, DisplayMemberPath = "Name", SelectedIndex = 0 };
        form.Add("Новый _статус", status);
        var reason = form.Text("_Причина отмены", null, 500, multiline: true);
        reason.IsEnabled = ((StatusOption?)status.SelectedItem)?.Status == OrderStatus.Cancelled;
        status.SelectionChanged += (_, _) => reason.IsEnabled =
            ((StatusOption?)status.SelectedItem)?.Status == OrderStatus.Cancelled;
        return form.Save("Применить", () =>
        {
            var next = ((StatusOption?)status.SelectedItem)?.Status ?? throw new RuleException("Выберите статус.");
            return new TransitionInput(next, next == OrderStatus.Cancelled
                ? InputRules.Required(reason.Text, "Причина отмены", 500) : null, order.Version);
        });
    }

    public void ShowOrder(OrderInfo order)
    {
        var form = new Form(owner, $"Заказ №{order.Id} · {order.Title}");
        form.Summary($"Статус: {WorkshopText.Status(order.Status)}");
        form.Summary($"Заказчик: {order.Customer}  ·  {order.Phone}");
        form.Summary($"Адрес установки: {order.Address}");
        form.Summary($"Сборщик: {order.Assembler ?? "—"}");
        form.Summary($"Сумма: {(order.Amount is { } amount ? amount.ToString("N2", Russian) + " ₽" : "—")}");
        form.Summary($"Дата доставки: {order.DeliveryDate?.ToString("dd.MM.yyyy") ?? "—"}");
        form.Summary($"Создан: {order.CreatedAt.ToLocalTime():dd.MM.yyyy HH:mm}");
        if (order.CompletedAt is { } completed)
            form.Summary($"Установлен: {completed.ToLocalTime():dd.MM.yyyy HH:mm}");
        if (!string.IsNullOrEmpty(order.CancellationReason))
            form.Summary($"Причина отмены: {order.CancellationReason}");
        if (!string.IsNullOrEmpty(order.Notes))
            form.Summary($"Примечания и размеры: {order.Notes}");
        form.Show();
    }

    public string? OpenCsv()
    {
        var dialog = new OpenFileDialog { Filter = "Файлы CSV (*.csv)|*.csv", CheckFileExists = true };
        return dialog.ShowDialog(owner) == true ? dialog.FileName : null;
    }

    public string? SaveExport(string format)
    {
        var dialog = new SaveFileDialog
        {
            FileName = $"orders-{DateTime.Today:yyyy-MM-dd}.{format}",
            Filter = $"{format.ToUpperInvariant()} (*.{format})|*.{format}",
            DefaultExt = format,
            AddExtension = true,
            OverwritePrompt = true
        };
        return dialog.ShowDialog(owner) == true ? dialog.FileName : null;
    }

    private sealed class Form
    {
        private readonly Window window;
        private readonly StackPanel fields = new();
        private readonly StackPanel footer = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        private readonly TextBlock message = new()
        {
            Foreground = new SolidColorBrush(Color.FromRgb(157, 44, 39)),
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 9)
        };

        public Form(Window owner, string title)
        {
            window = new Window
            {
                Owner = owner, Title = title, WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Width = 540, SizeToContent = SizeToContent.Height, MaxHeight = SystemParameters.WorkArea.Height - 40,
                ResizeMode = ResizeMode.NoResize, Style = (Style)Application.Current.FindResource(typeof(Window))
            };
            var layout = new DockPanel { Margin = new Thickness(22) };
            var buttons = new StackPanel();
            buttons.Children.Add(message);
            buttons.Children.Add(footer);
            DockPanel.SetDock(buttons, Dock.Bottom);
            layout.Children.Add(buttons);
            layout.Children.Add(new ScrollViewer { Content = fields, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            window.Content = layout;
        }

        public void Add(string caption, Control control)
        {
            fields.Children.Add(new Label { Content = caption, Target = control, Padding = new Thickness(0, 7, 0, 4) });
            fields.Children.Add(control);
        }

        public TextBox Text(string caption, string? value, int max, bool multiline = false)
        {
            var control = new TextBox
            {
                Text = value ?? "", MaxLength = max, AcceptsReturn = multiline,
                TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap
            };
            if (multiline)
            {
                control.MinHeight = 55;
                control.MaxHeight = 90;
                control.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            }
            Add(caption, control);
            return control;
        }

        public void Summary(string value) =>
            fields.Children.Add(new TextBlock { Text = value, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 8) });

        public T? Save<T>(string label, Func<T> value) where T : class
        {
            T? result = null;
            var cancel = new Button { Content = "Закрыть", IsCancel = true, Margin = new Thickness(0, 0, 8, 0) };
            var save = new Button { Content = label, IsDefault = true, Style = (Style)Application.Current.FindResource("PrimaryButton") };
            save.Click += (_, _) =>
            {
                try
                {
                    result = value();
                    window.DialogResult = true;
                }
                catch (RuleException error)
                {
                    message.Text = error.Message;
                }
            };
            footer.Children.Add(cancel);
            footer.Children.Add(save);
            return window.ShowDialog() == true ? result : null;
        }

        public void Show()
        {
            footer.Children.Add(new Button { Content = "Закрыть", IsCancel = true, IsDefault = true });
            window.ShowDialog();
        }
    }
}
