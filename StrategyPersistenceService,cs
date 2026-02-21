using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;
using Common.Models;

namespace App64.Services
{
    /// <summary>
    /// .NET Framework 내장 DataContractJsonSerializer를 사용하여 
    /// 전략 리스트를 파일로 저장하고 불러오는 관리 서비스.
    /// </summary>
    public static class StrategyPersistenceService
    {
        private static readonly string FilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "strategies.json");

        public static void SaveStrategies(List<StrategyDefinition> strategies)
        {
            try
            {
                var serializer = new DataContractJsonSerializer(typeof(List<StrategyDefinition>));
                using (var stream = new FileStream(FilePath, FileMode.Create))
                {
                    serializer.WriteObject(stream, strategies);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"전략 저장 실패: {ex.Message}");
            }
        }

        public static List<StrategyDefinition> LoadStrategies()
        {
            if (!File.Exists(FilePath)) return new List<StrategyDefinition>();

            try
            {
                var serializer = new DataContractJsonSerializer(typeof(List<StrategyDefinition>));
                using (var stream = new FileStream(FilePath, FileMode.Open))
                {
                    return (List<StrategyDefinition>)serializer.ReadObject(stream) ?? new List<StrategyDefinition>();
                }
            }
            catch
            {
                return new List<StrategyDefinition>();
            }
        }
    }
}
