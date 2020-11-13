using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace RabbitLight.Helpers
{
    public static class ArgumentHelper
    {
        public static IDictionary<string, object> ParseArguments(string arguments)
        {
            try
            {
                if (arguments == null) return null;

                var cleanStr = Regex.Replace(arguments, @"\s+", "");
                var kvList = cleanStr.Split(';').Select(x => x.Split(':'));

                var invalidArgs = kvList.Where(x => x.Length != 2);
                if (invalidArgs.Any())
                    throw new ArgumentException($"Invalid arguments string: {string.Join("; ", invalidArgs.Select(x => string.Join(": ", x)))}");

                var dict = kvList.ToDictionary(kv => kv[0], kv => ParseArgumentValue(kv[1]));

                return dict;
            }
            catch (JsonReaderException ex)
            {
                throw new JsonReaderException("[RabbitLight] Error while deserializing declaration arguments list", ex);
            }
            catch (Exception ex)
            {
                throw new Exception("[RabbitLight] Error while parsing declaration arguments", ex);
            }
        }

        public static object ParseArgumentValue(string value)
        {
            var isBoolean = Boolean.TryParse(value, out var boolValue);
            if (isBoolean) return boolValue;

            var isInt = Int32.TryParse(value, out var intValue);
            if (isInt) return intValue;

            var isDecimal = Decimal.TryParse(value, out var decimalValue);
            if (isDecimal) return decimalValue;

            var isList = Regex.IsMatch(value, @"^\[.*\]$");
            if (isList)
            {
                var strList = JsonConvert.DeserializeObject<IEnumerable<string>>(value);
                var listValue = strList.Select(x => ParseArgumentValue(x)).ToList();
                return listValue;
            }

            return value;
        }
    }
}
