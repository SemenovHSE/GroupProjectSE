﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using GroupProject.Database;
using GroupProject.Logging;
using GroupProject.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using DatabaseImageProcessingResult = GroupProject.Database.Models.ImageProcessingResult;

namespace GroupProject.ExternalServices
{
    public class ImageProcessorService
    {
        private HttpClient Client { get; }

        private ILogger Logger { get; }

        private ClaimsPrincipal AuthenticatedUser { get; set; }

        private DatabaseContext Context { get; }


        public ImageProcessorService(IConfiguration configuration, HttpClient client, ILogger logger, DatabaseContext context, IHttpContextAccessor httpContextAccessor)
        {
            Client = client;
            Client.BaseAddress = new Uri(configuration.GetSection("ExternalServices:ImageProcessorServiceUrl").Value);
            Logger = logger;
            Context = context;
            AuthenticatedUser = httpContextAccessor.HttpContext.User;
        }


        /// <summary>
        /// Получение результата обработки изображения
        /// </summary>
        /// <param name="image">Поток байт изображения</param>
        /// <param name="fileName">Название изображения</param>
        /// <returns>Результат обработки изображения</returns>
        public async Task<ServiceImageProcessingResult> GetImageResult(MemoryStream image, string fileName)
        {
            try
            {
                var content = new MultipartFormDataContent();
                var imageContent = new ByteArrayContent(image.ToArray());
                content.Add(imageContent, "image_data", fileName);
                var response = await Client.PostAsync(Client.BaseAddress + "GetPrediction", content);
                if (response.StatusCode != HttpStatusCode.OK)
                {
                    throw new Exception("Не удалось получить результат обработки");
                }

                var responseContent = await response.Content.ReadAsStringAsync();

                var imageProcessingResult = JsonConvert.DeserializeObject<ServiceImageProcessingResult>(responseContent);

                if (AuthenticatedUser != null)
                {
                    DatabaseImageProcessingResult result = new DatabaseImageProcessingResult()
                    {
                        Account = await Context.Accounts.FirstAsync(a => a.Username == AuthenticatedUser.Identity.Name),
                        ProcessingDateTime = imageProcessingResult.ProcessingDateTime,
                        Image = imageProcessingResult.Image,
                        ProcessingResult = JsonConvert.SerializeObject(imageProcessingResult.ProcessingResult)
                    };
                    Context.ImageProcessingResults.Add(result);
                    await Context.SaveChangesAsync();

                    imageProcessingResult.Id = result.Id;
                }

                return imageProcessingResult;
            }
            catch (Exception e)
            {
                Logger.Error(e.ToString());
                throw;
            }
        }
    }
}
