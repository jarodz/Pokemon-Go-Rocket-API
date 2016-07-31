using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PokemonGo.RocketAPI.Exceptions
{
    public class InvalidResponseException : Exception
    {
        public InvalidResponseException(string message) : base(message)
        {
        }
    }
}
