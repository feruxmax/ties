using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using WebAppHosted.Client.Models;

namespace WebAppHosted.Client.Pages
{
    public partial class SearchOrAddInput
    {
        [Parameter]
        public EventCallback<string> OnAdd { get; set; }

        [Parameter]
        public ICollection<Item> Items { get; set; }

        private string addIsHiddenClass => _filterdedItems.Count == 0 && _currentValue != string.Empty
            ? string.Empty
            : "d-none";

        private string _currentValue = String.Empty;

        private ICollection<Item> _filterdedItems => _currentValue != string.Empty
            ? Items
                .Where(x => x.Label.StartsWith(_currentValue))
                .Take(5)
                .ToArray()
            : Array.Empty<Item>();

        private async Task AddValue()
        {
            await OnAdd.InvokeAsync(_currentValue);
            _currentValue = string.Empty;
        }
    }

    public class Item
    {
        public string Label { get; set; }
    }
}