using MDD4All.Bitbucket.DataModels;
using MDD4All.DevOpsObserver.DataModels;
using MDD4All.DevOpsObserver.DataProviders.Contracts;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace MDD4All.DevOpsObserver.DataProviders.Bitbucket
{
    public class BitbucketStatusProvider : IDevOpsStatusProvider
    {
        private IConfiguration _configuration;
        private HttpClient _httpClient;

        public BitbucketStatusProvider(IConfiguration configuration, HttpClient httpClient)
        {
            _configuration = configuration;
            _httpClient = httpClient;
        }

        public async Task<List<DevOpsStatusInformation>> GetDevOpsStatusListAsync(DevOpsSystem devOpsSystem)
        {
            List<DevOpsStatusInformation> result = new List<DevOpsStatusInformation>();

            foreach (DevOpsObservable devOpsObservable in devOpsSystem.ObservedAutomations)
            {
                HttpRequestMessage request = new HttpRequestMessage
                {
                    Method = new HttpMethod("GET"),
                    RequestUri = new Uri(devOpsSystem.ServerURL + "/2.0/repositories/" + devOpsSystem.Tenant + "/" + devOpsObservable.RepositoryName +
                                        "/pipelines/?pagelen=100&sort=-created_on"),
                };

                string clientID = _configuration[devOpsSystem.GUID + ":LoginName"];
                string clientSecret = _configuration[devOpsSystem.GUID + ":Password"];

                string authenticationString = $"{clientID}:{clientSecret}";
                string base64EncodedAuthenticationString = Convert.ToBase64String(ASCIIEncoding.ASCII.GetBytes(authenticationString));

                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", 
                                                                                                      base64EncodedAuthenticationString);
                try
                {
                    HttpResponseMessage response = await _httpClient.SendAsync(request);
                    HttpStatusCode responseStatusCode = response.StatusCode;

                    if (responseStatusCode == HttpStatusCode.OK)
                    {
                        string responseBody = await response.Content.ReadAsStringAsync();
                        PipelineDataResponse pipelineDataResponse = JsonConvert.DeserializeObject<PipelineDataResponse>(responseBody);

                        if (pipelineDataResponse != null && pipelineDataResponse.Values.Count > 0)
                        {
                            DevOpsStatusInformation devOpsStatusInformation = ConvertPipelineResponseToStatus(pipelineDataResponse.Values[0]);
                            devOpsStatusInformation.Alias = devOpsObservable.Alias;
                            result.Add(devOpsStatusInformation);
                        }
                        else
                        {
                            CreateUnknownStateData(result, devOpsObservable);
                        }

                    }
                    else
                    {
                        CreateUnknownStateData(result, devOpsObservable);
                    }
                }
                catch (Exception exception)
                {
                    CreateUnknownStateData(result, devOpsObservable);
                }
            }

            return result;
        }

        private void CreateUnknownStateData(List<DevOpsStatusInformation> result, DevOpsObservable devOpsObservable)
        {
            DevOpsStatusInformation devOpsStatusInformation = new DevOpsStatusInformation
            {
                RepositoryName = devOpsObservable.RepositoryName,
                Branch = devOpsObservable.RepositoryBranch,
                Alias = devOpsObservable.Alias,
                GitServerType = "Bitbucket",

            };
            result.Add(devOpsStatusInformation);
        }

        private DevOpsStatusInformation ConvertPipelineResponseToStatus(Value statusValue)
        {
            DevOpsStatusInformation result = new DevOpsStatusInformation();

            result.RepositoryName = statusValue.Repository.FullName;
            result.Branch = statusValue.Target.RefName;
            result.GitServerType = "Bitbucket";
            result.ShortName = statusValue.Repository.Name.ToString();
            result.BuildNumber = statusValue.BuildNumber;
            result.BuildTime = statusValue.CreatedOn;
            result.ID = statusValue.Repository.Uuid;
            
            if (statusValue.State != null && statusValue.State.Name == "IN_PROGRESS")
            {
                result.StatusValue = DevOpsStatus.InProgress;
            }
            else if (statusValue.State != null && statusValue.State.Name == "COMPLETED")
            {
                if (statusValue.State.Result != null && statusValue.State.Result.Name == "SUCCESSFUL")
                {
                    result.StatusValue = DevOpsStatus.Success;
                    result.LastSeenSuccessfulBuild = DateTime.Now;
                }
                else if (statusValue.State.Result != null && statusValue.State.Result.Name == "FAILED")
                {
                    result.StatusValue = DevOpsStatus.Fail;
                }
                else if (statusValue.State.Result != null && statusValue.State.Result.Name == "ERROR")
                {
                    result.StatusValue = DevOpsStatus.Error;
                }
            }

            return result;
        }
    }
}
