using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Logistics.Constants;
using Logistics.DAL.Interfaces;
using Logistics.Models;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;

namespace Logistics.DAL
{
  public class PlanesDAL : IPlanesDAL
  {
    private readonly IMongoClient mongoClient;
    private readonly IMongoDatabase mongodataBase;
    private readonly IMongoCollection<BsonDocument> planesCollection;

    private readonly ILogger<PlanesDAL> logger;
    private string lastError;

    public PlanesDAL(IMongoClient mongoClient, ILogger<PlanesDAL> logger)
    {
      this.mongoClient = mongoClient;
      this.mongodataBase = mongoClient.GetDatabase(CommonConstants.Database);
      var databaseWithWriteConcern = this.mongodataBase.WithWriteConcern(WriteConcern.WMajority).WithReadConcern(ReadConcern.Majority);
      this.planesCollection = databaseWithWriteConcern.GetCollection<BsonDocument>(PlanesConstants.CollectionName);
      this.logger = logger;
    }

    public async Task<List<Plane>> GetPlanes()
    {
      var planes = new ConcurrentBag<Plane>();
      try
      {
        var planeDtosCursor = await this.planesCollection.FindAsync(new BsonDocument());
        var planeDtos = planeDtosCursor.ToList();
        // Parallelizing the serialization to make it faster.
        Parallel.ForEach(planeDtos, planeDto =>
        {
          Plane planeModel = this.FetchPlane(planeDto);
          if (planeModel != null)
          {
            planes.Add(planeModel);
          }
        });
      }
      catch (MongoException ex)
      {
        lastError = $"Failed to fetch all the planes.Exception: {ex.ToString()}";
        this.logger.LogError(lastError);
      }

      return planes.OrderBy(x => x.Callsign).ToList();
    }

    public async Task<Plane> GetPlaneById(string id)
    {
      var filter = new BsonDocument();
      filter[CommonConstants.UnderScoreId] = id;
      try
      {
        // Will use _id index
        var cursor = await this.planesCollection.FindAsync(filter);
        var planes = cursor.ToList();
        if (planes.Any())
        {
          var planeModel = this.FetchPlane(planes.FirstOrDefault());
          return planeModel;
        }
      }
      catch (MongoException ex)
      {
        lastError = $"Failed to fetch the plane by id: {id}.Exception: {ex.ToString()}";
        this.logger.LogError(lastError);
      }

      return null;
    }

    public async Task<bool> UpdatePlaneLocation(string id, List<double> location, float heading)
    {
      var result = false;
      try
      {
        var filter = Builders<BsonDocument>.Filter.Eq(CommonConstants.UnderScoreId, id);
        var update = Builders<BsonDocument>.Update
                                        .Set(PlanesConstants.CurrentLocation, location)
                                        .Set(PlanesConstants.Heading, heading);
        var updatedPlaneResult = await this.planesCollection.UpdateOneAsync(filter, update);
        result = updatedPlaneResult.IsAcknowledged;
      }
      catch (MongoException ex)
      {
        lastError = $"Failed to update the location/heading info for plane: {id}.Exception: {ex.ToString()}";
        this.logger.LogError(lastError);
        result = false;
      }

      return result;
    }

    public async Task<bool> UpdatePlaneLocationAndLanding(string id, List<double> location, float heading, string city)
    {
      var result = false;
      try
      {
        var filter = Builders<BsonDocument>.Filter.Eq(CommonConstants.UnderScoreId, id);
        var update = Builders<BsonDocument>.Update
                             .Set(PlanesConstants.CurrentLocation, location)
                             .Set(PlanesConstants.Heading, heading)
                             .Set(PlanesConstants.Landed, city);
        var updatedPlaneResult = await this.planesCollection.UpdateOneAsync(filter, update);
        result = updatedPlaneResult.IsAcknowledged;
      }
      catch (MongoException ex)
      {
        lastError = $"Failed to update the location/heading/city info for plane: {id}.Exception: {ex.ToString()}";
        this.logger.LogError(lastError);
        result = false;
      }

      return result;
    }

    public async Task<bool> AddPlaneRoute(string id, string city)
    {
      var result = false;
      try
      {
        var filter = Builders<BsonDocument>.Filter.Eq(CommonConstants.UnderScoreId, id);
        var update = Builders<BsonDocument>.Update
                             .AddToSet(PlanesConstants.Route, city);
        var updatedPlaneResult = await this.planesCollection.UpdateOneAsync(filter, update);
        result = updatedPlaneResult.IsAcknowledged;
      }
      catch (MongoException ex)
      {
        lastError = $"Failed to add plane route : {city} for the plane: {id}.Exception: {ex.ToString()}";
        this.logger.LogError(lastError);
        result = false;
      }
      return result;
    }

    public async Task<bool> ReplacePlaneRoutes(string id, string city)
    {
      var result = false;
      try
      {
        var filter = Builders<BsonDocument>.Filter.Eq(CommonConstants.UnderScoreId, id);
        var update = Builders<BsonDocument>.Update
                             .Set(PlanesConstants.Route, new List<string> { city });
        var updatedPlaneResult = await this.planesCollection.UpdateOneAsync(filter, update);
        result = updatedPlaneResult.IsAcknowledged;
      }
      catch (MongoException ex)
      {
        lastError = $"Failed to replace plane route : {city} for the plane: {id}.Exception: {ex.ToString()}";
        this.logger.LogError(lastError);
        result = false;
      }
      return result;
    }

    public async Task<bool> RemoveFirstPlaneRoute(string id)
    {
      var result = false;
      try
      {
        var plane = await this.GetPlaneById(id);
        var filter = Builders<BsonDocument>.Filter.Eq(CommonConstants.UnderScoreId, id);
        UpdateDefinition<BsonDocument> update;
        update = Builders<BsonDocument>.Update
                                   .PopFirst(PlanesConstants.Route);

        var updatedPlaneResult = await this.planesCollection.UpdateOneAsync(filter, update);
        result = updatedPlaneResult.IsAcknowledged;
      }
      catch (MongoException ex)
      {
        lastError = $"Failed to remove the first route  for the plane: {id}.Exception: {ex.ToString()}";
        this.logger.LogError(lastError);
        result = false;
      }
      return result;
    }

    public string GetLastError()
    {
      return lastError;
    }

    private Plane FetchPlane(BsonDocument planeDto)
    {
      var planeModel = BsonSerializer.Deserialize<Plane>(planeDto);
      planeModel.Heading = Convert.ToDecimal(string.Format("{0:N2}", planeDto.GetValue(PlanesConstants.Heading).ToDecimal()));
      return planeModel;
    }
  }
}