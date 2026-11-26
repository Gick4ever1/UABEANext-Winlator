using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Metadata;
using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using UABEANext4.Interfaces;

namespace UABEANext4.ViewModels.Dialogs;

public partial class MessageBoxViewModel : ViewModelBase, IDialogAware<MessageBoxResult?>
{
    [ObservableProperty]
    public string _msgTitle = "Message box title";
    [ObservableProperty]
    public string _msgText = "Message box text";
    [ObservableProperty]
    public MessageBoxType _msgType = MessageBoxType.OKCancel;
    [ObservableProperty]
    public string _buttonTextA = "";
    [ObservableProperty]
    public string _buttonTextB = "";
    [ObservableProperty]
    public string _buttonTextC = "";

    public string Title => MsgTitle;
    public int Width => 400;
    public int Height => 160;

    public bool IsModal => false;

    public event Action<MessageBoxResult?>? RequestClose;

    private bool _closed;

    // Close only once: the original code invoked RequestClose twice per click
    // (the real result, then Unknown), which can hang under Wine.
    private void CloseOnce(MessageBoxResult result)
    {
        if (_closed)
            return;

        _closed = true;
        RequestClose?.Invoke(result);
    }

    public MessageBoxViewModel()
    {
    }

    public MessageBoxViewModel(string title, string text, MessageBoxType type)
    {
        MsgTitle = title;
        MsgText = text;
        MsgType = type;
    }

    public MessageBoxViewModel(string title, string text, MessageBoxType type, List<string> buttonTexts)
    {
        MsgTitle = title;
        MsgText = text;
        MsgType = type;
        while (buttonTexts.Count < 3)
        {
            buttonTexts.Add("");
        }
        ButtonTextA = buttonTexts[0];
        ButtonTextB = buttonTexts[1];
        ButtonTextC = buttonTexts[2];
    }

    public void BtnA_Click()
    {
        if (MsgType == MessageBoxType.OK)
        {
            CloseOnce(MessageBoxResult.OK);
        }
        else if (MsgType == MessageBoxType.OKCancel)
        {
            CloseOnce(MessageBoxResult.OK);
        }
        else if (MsgType == MessageBoxType.YesNo)
        {
            CloseOnce(MessageBoxResult.Yes);
        }
        else if (MsgType == MessageBoxType.YesNoCancel)
        {
            CloseOnce(MessageBoxResult.Yes);
        }
        else if (MsgType == MessageBoxType.Custom)
        {
            CloseOnce(MessageBoxResult.CustomButtonA);
        }
        CloseOnce(MessageBoxResult.Unknown);
    }

    public void BtnB_Click()
    {
        if (MsgType == MessageBoxType.OKCancel)
        {
            CloseOnce(MessageBoxResult.Cancel);
        }
        else if (MsgType == MessageBoxType.YesNo)
        {
            CloseOnce(MessageBoxResult.No);
        }
        else if (MsgType == MessageBoxType.YesNoCancel)
        {
            CloseOnce(MessageBoxResult.No);
        }
        else if (MsgType == MessageBoxType.Custom)
        {
            CloseOnce(MessageBoxResult.CustomButtonB);
        }
        CloseOnce(MessageBoxResult.Unknown);
    }

    public void BtnC_Click()
    {
        if (MsgType == MessageBoxType.YesNoCancel)
        {
            CloseOnce(MessageBoxResult.Cancel);
        }
        else if (MsgType == MessageBoxType.Custom)
        {
            CloseOnce(MessageBoxResult.CustomButtonC);
        }
        CloseOnce(MessageBoxResult.Unknown);
    }
}

public class MessageBoxTemplateSelector : IDataTemplate
{
    [Content]
    public Dictionary<string, IDataTemplate> AvailableTemplates { get; } = new();

    public Control Build(object? param)
    {
        var key = param?.ToString() ?? throw new ArgumentNullException(nameof(param));
        return AvailableTemplates[key].Build(param)!;
    }

    public bool Match(object? data)
    {
        var key = data?.ToString();

        return data is MessageBoxType
                && !string.IsNullOrEmpty(key)
                && AvailableTemplates.ContainsKey(key);
    }
}

public enum MessageBoxType
{
    OK,
    OKCancel,
    YesNo,
    YesNoCancel,
    Custom
}

public enum MessageBoxResult
{
    Unknown,
    OK,
    Yes,
    No,
    Cancel,
    CustomButtonA,
    CustomButtonB,
    CustomButtonC
}