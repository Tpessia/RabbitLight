using Microsoft.AspNetCore.Mvc;
using RabbitLight.AspNetCore.Consumers;
using RabbitLight.AspNetCore.Consumers.Context;
using RabbitLight.AspNetCore.Consumers.Routes;
using RabbitLight.AspNetCore.Models;
using RabbitLight.Publisher;
using System;
using System.IO;

namespace RabbitLight.AspNetCore.Controllers
{
    [ApiController]
    [Route("integrity")]
    public class IntegrityTestController : ControllerBase
    {
        private readonly IPublisher _publisher;

        public IntegrityTestController(AspNetAppContext busContext)
        {
            _publisher = busContext.Publisher;
        }

        [HttpGet]
        public string Integrity()
        {
            System.IO.File.WriteAllText(IntegrityTestConsumer.OutputPath, "");

            int counter = 0;
            string line;

            var file = new StreamReader(IntegrityTestConsumer.InputPath);
            while ((line = file.ReadLine()) != null)
            {
                var body = new TestMessage { Content = line };
                _publisher.PublishJson(Exchanges.Test1, RoutingKeys.Integrity, body);
                counter++;
            }

            return "Message published!";
        }

        [HttpGet("generate")]
        public string Generate(int size = 1000)
        {
            var text = "";
            
            for (var i = 0; i < size; i++)
                text += i.ToString() + Environment.NewLine;
            
            System.IO.File.WriteAllText(IntegrityTestConsumer.InputPath, text);

            return "File generated!";
        }

        // JS to check integrity result
        //function checkSequence(arr) {
        //    return {
        //        isSequence: isSequence(arr),
        //        duplicates: findDuplicates(arr),
        //        sorted: arr.sort((a,b) => a - b)
        //    };

        //    function isSequence(arr) {
        //        return arr.sort((a,b) => a - b)
        //            .reduce((acc, val) => acc = [acc[0] + val - acc[1], val], [0,-1])[0]
        //            === arr.length;
        //    }

        //    function findDuplicates(arr) {
        //      let sortedArr = arr.slice().sort((a,b) => a - b);
        //      let results = [];
        //      for (let i = 0; i < sortedArr.length - 1; i++) {
        //        if (sortedArr[i + 1] == sortedArr[i]) {
        //          results.push(sortedArr[i]);
        //        }
        //      }
        //      return results;
        //    }
        //}
    }
}
