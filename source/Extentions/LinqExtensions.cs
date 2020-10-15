using System;
using System.Collections.Generic;

namespace RabbitLight.Extensions
{
    internal static class LinqExtensions
    {
        public static void RemoveAll<T,U>(this Dictionary<T,U> dictionary, Func<KeyValuePair<T, U>, bool> shouldDelete)
        {
            var deleteKeys = new List<T>();

            foreach (var item in dictionary)
                if (shouldDelete(item))
                    deleteKeys.Add(item.Key);

            foreach (var key in deleteKeys)
                dictionary.Remove(key);
        }
    }
}
