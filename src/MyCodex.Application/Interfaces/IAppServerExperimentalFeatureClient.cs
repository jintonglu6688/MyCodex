using System.Collections.Generic;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;

namespace MyCodex.Application.Interfaces
{
    public interface IAppServerExperimentalFeatureClient
    {
        Task<List<AppServerExperimentalFeature>> ListExperimentalFeaturesAsync(uint? limit = null);
    }
}
