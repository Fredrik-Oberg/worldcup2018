using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using RedisLayer;

namespace web.Controllers
{
    [Route("api/[controller]")]
    public class ResultsController : Controller
    {
        [HttpGet("[action]")]
        public IEnumerable<Participant> All()
        {
            var redis = new Redis(10);
            var keys = redis.GetAllKeys();
            var participants = keys.Where(x => x.Contains("participants:", StringComparison.OrdinalIgnoreCase)).Select(x => redis.GetRedisValue<Participant>(x)).ToList();
            var orderedParticipants = new List<Participant>();
            foreach (var participant in participants)
            {
                var orderedMatches = new List<Match>(participant.Matches.OrderByDescending(x => x.MatchStart));
                participant.Matches = orderedMatches;
                orderedParticipants.Add(participant);
            }
            return orderedParticipants.OrderByDescending(x => x.Points);
        }
    }
}
