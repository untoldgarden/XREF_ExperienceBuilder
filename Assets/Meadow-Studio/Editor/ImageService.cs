//-----------------------------------------------------------------------
// <copyright file="ImageService.cs" company="Untold Garden LTD">
//
// Copyright 2024 Untold Garden LTD
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
// http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
//
// </copyright>
//-----------------------------------------------------------------------


using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;


namespace Meadow.Studio
{
    public static  class ImageService
    {
        public static readonly Dictionary<string, Texture2D> imageCache = new Dictionary<string, Texture2D>();   
        
        // Download image from URL
        public static async Task<HTTPSResponse> GetImage(string url)
        {
            if(string.IsNullOrEmpty(url))
            {
                return new HTTPSResponse { success = false, data = null };
            }
            if (imageCache.ContainsKey(url))
            {
                return new HTTPSResponse { success = true, data = imageCache[url] };
            }
            using (UnityWebRequest www = UnityWebRequestTexture.GetTexture(url))            {
                await www.SendWebRequest();
                try
                {
                    return new HTTPSResponse { success = true, data = DownloadHandlerTexture.GetContent(www) };
                }
                catch
                {
                    return new HTTPSResponse { success = false, data = null };
                }
            }
        }

        public static void AddImageToCache(string url, Texture2D texture)
        {
            if (!imageCache.ContainsKey(url))
            {
                imageCache.Add(url, texture);
            }
        }

        public static void ClearCache()
        {
            imageCache.Clear();
        }
    }
}
