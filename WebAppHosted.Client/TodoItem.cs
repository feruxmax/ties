using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;

public class TodoItem
{
    public string Title { get; set; }
    public bool IsDone { get; set; }
}

class TodoItemViewModel
{
    private readonly TodoItem _item;

    public string Title
    {
        get => _item.Title;
        set => SetField(_item, i => i.Title, value, () => IsSaved = false);
    }

    public bool IsDone 
    {
        get => _item.IsDone;
        set => SetField(_item, i => _item.IsDone, value, () => IsSaved = false);
    }

    public bool IsSaved { get; set; }

    public TodoItemViewModel(TodoItem item)
    {
        _item = item;
    }

    public TodoItem Unwrap() => _item;

    protected bool SetField<T, TResult>(T target, Expression<Func<T, TResult>> property, TResult value, Action action)
    {
        if (EqualityComparer<TResult>.Default.Equals(property.Compile()(target), value)) return false;

        var expr = (MemberExpression) property.Body;
        var prop = (PropertyInfo)expr.Member;
        prop.SetValue(target, value, null);

        action();
        
        return true;
    }
}