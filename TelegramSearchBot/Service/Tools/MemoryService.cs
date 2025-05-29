using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using TelegramSearchBot.Attributes;
using TelegramSearchBot.Interface;
using TelegramSearchBot.Interface.Tools;
using TelegramSearchBot.Model;
using TelegramSearchBot.Model.Data;

namespace TelegramSearchBot.Service.Tools
{
    public class Entity
    {
        public string Name { get; set; }
        public string EntityType { get; set; }
        public List<string> Observations { get; set; } = new List<string>();
    }

    public class Relation
    {
        public string From { get; set; }
        public string To { get; set; }
        public string RelationType { get; set; }
    }

    public class KnowledgeGraph
    {
        public List<Entity> Entities { get; set; } = new List<Entity>();
        public List<Relation> Relations { get; set; } = new List<Relation>();
    }

    [Injectable(Microsoft.Extensions.DependencyInjection.ServiceLifetime.Transient)]
    public class MemoryService : IService, IMemoryService
    {
        public string ServiceName => "MemoryService";

        private readonly KnowledgeGraphManager _graphManager;
        private readonly DataDbContext _dbContext;

        public MemoryService(DataDbContext dbContext)
        {
            _dbContext = dbContext;
            _graphManager = new KnowledgeGraphManager(_dbContext);
        }

        [McpTool(@"
A knowledge graph tool that stores and retrieves information in a structured way using entities, relations and observations.

Data Types:
- Entity: { name: string, entityType: string, observations: string[] }
- Relation: { from: string, to: string, relationType: string }
- ObservationInput: { entityName: string, contents: string[] }
- ObservationDeletion: { entityName: string, observations: string[] }
- ObservationResult: { entityName: string, addedObservations: string[] }
- SearchQuery: { query: string }
- KnowledgeGraph: { entities: Entity[], relations: Relation[] }

Available Commands:
1. create_entities: 
   - Creates new entities
   - Input: { entities: Entity[] }
   - Returns: Entity[] (created entities)

2. create_relations:
   - Creates relations between entities (use active voice)
   - Input: { relations: Relation[] }
   - Returns: Relation[] (created relations)

3. add_observations:
   - Adds observations to existing entities
   - Input: { observations: ObservationInput[] }
   - Returns: ObservationResult[] 

4. delete_entities:
   - Deletes entities and their relations
   - Input: { entityNames: string[] }
   - Returns: success message

5. delete_observations:
   - Deletes specific observations
   - Input: { deletions: ObservationDeletion[] }
   - Returns: success message

6. delete_relations:
   - Deletes relations
   - Input: { relations: Relation[] }
   - Returns: success message

7. read_graph:
   - Reads entire knowledge graph
   - Input: {}
   - Returns: KnowledgeGraph

8. search_nodes:
   - Searches graph with query
   - Input: { query: string }
   - Returns: KnowledgeGraph (filtered)

9. open_nodes:
   - Gets specific nodes by name
   - Input: { names: string[] }
   - Returns: KnowledgeGraph (filtered)
")]
        public async Task<object> ProcessMemoryCommandAsync(
            [McpParameter(@"
The memory command to execute. Must be one of:
- create_entities
- create_relations  
- add_observations
- delete_entities
- delete_observations
- delete_relations
- read_graph
- search_nodes
- open_nodes
")] string command,
            [McpParameter(@"
Command arguments in JSON format. Structure depends on command:

For create_entities:
{
  entities: [
    {
      name: string (required),
      entityType: string (required),
      observations: string[] (optional)
    }
  ]
}

For create_relations:
{
  relations: [
    {
      from: string (required),
      to: string (required), 
      relationType: string (required)
    }
  ]
}

For add_observations:
{
  observations: [
    {
      entityName: string (required),
      contents: string[] (required)
    }
  ]
}

For delete_entities:
{
  entityNames: string[] (required)
}

For delete_observations:
{
  deletions: [
    {
      entityName: string (required),
      observations: string[] (required)
    }
  ]
}

For delete_relations:
{
  relations: [
    {
      from: string (required),
      to: string (required),
      relationType: string (required)
    }
  ]
}

For read_graph: {} (no arguments)

For search_nodes:
{
  query: string (required)
}

For open_nodes:
{
  names: string[] (required)
}
")] string arguments,
            ToolContext toolContext)
        {
            try
            {
                switch (command.ToLower())
                {
                    case "create_entities":
                        var entities = JsonSerializer.Deserialize<List<Entity>>(arguments);
                        return await _graphManager.CreateEntitiesAsync(entities, toolContext.ChatId);
                    case "create_relations":
                        var relations = JsonSerializer.Deserialize<List<Relation>>(arguments);
                        return await _graphManager.CreateRelationsAsync(relations, toolContext.ChatId);
                    case "add_observations":
                        var obsInput = JsonSerializer.Deserialize<List<ObservationInput>>(arguments);
                        return await _graphManager.AddObservationsAsync(obsInput, toolContext.ChatId);
                    case "delete_entities":
                        var entityNames = JsonSerializer.Deserialize<List<string>>(arguments);
                        await _graphManager.DeleteEntitiesAsync(entityNames, toolContext.ChatId);
                        return "Entities deleted successfully";
                    case "delete_observations":
                        var obsDeletions = JsonSerializer.Deserialize<List<ObservationDeletion>>(arguments);
                        await _graphManager.DeleteObservationsAsync(obsDeletions, toolContext.ChatId);
                        return "Observations deleted successfully";
                    case "delete_relations":
                        var delRelations = JsonSerializer.Deserialize<List<Relation>>(arguments);
                        await _graphManager.DeleteRelationsAsync(delRelations, toolContext.ChatId);
                        return "Relations deleted successfully";
                    case "read_graph":
                        return await _graphManager.ReadGraphAsync(toolContext.ChatId);
                    case "search_nodes":
                        var searchQuery = JsonSerializer.Deserialize<SearchQuery>(arguments);
                        return await _graphManager.SearchNodesAsync(searchQuery.Query, toolContext.ChatId);
                    case "open_nodes":
                        var nodeNames = JsonSerializer.Deserialize<List<string>>(arguments);
                        return await _graphManager.OpenNodesAsync(nodeNames, toolContext.ChatId);
                    default:
                        throw new Exception($"Unknown command: {command}");
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Memory command failed: {ex.Message}");
            }
        }
    }

    [Injectable(Microsoft.Extensions.DependencyInjection.ServiceLifetime.Transient)]
    public class KnowledgeGraphManager : IService
    {
        public string ServiceName => "KnowledgeGraphManager";
        
        private readonly DataDbContext _dbContext;

        public KnowledgeGraphManager(DataDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        private async Task<KnowledgeGraph> LoadGraphAsync(long chatId)
        {
            var graph = new KnowledgeGraph();
            
            var dbEntities = await _dbContext.MemoryGraphs
                .Where(x => x.ChatId == chatId && x.ItemType == "entity")
                .ToListAsync();

            var dbRelations = await _dbContext.MemoryGraphs
                .Where(x => x.ChatId == chatId && x.ItemType == "relation")
                .ToListAsync();

            graph.Entities = dbEntities.Select(e => new Entity
            {
                Name = e.Name,
                EntityType = e.EntityType,
                Observations = e.Observations?.Split('|').ToList() ?? new List<string>()
            }).ToList();

            graph.Relations = dbRelations.Select(r => new Relation
            {
                From = r.FromEntity,
                To = r.ToEntity,
                RelationType = r.RelationType
            }).ToList();

            return graph;
        }

        private async Task SaveEntityAsync(long chatId, Entity entity)
        {
            var existing = await _dbContext.MemoryGraphs
                .FirstOrDefaultAsync(x => 
                    x.ChatId == chatId && 
                    x.ItemType == "entity" && 
                    x.Name == entity.Name);

            if (existing == null)
            {
                _dbContext.MemoryGraphs.Add(new Model.Data.MemoryGraph
                {
                    ChatId = chatId,
                    Name = entity.Name,
                    EntityType = entity.EntityType,
                    Observations = string.Join("|", entity.Observations),
                    ItemType = "entity",
                    CreatedTime = DateTime.UtcNow
                });
            }
            else
            {
                existing.EntityType = entity.EntityType;
                existing.Observations = string.Join("|", entity.Observations);
            }
        }

        private async Task SaveRelationAsync(long chatId, Relation relation)
        {
            var existing = await _dbContext.MemoryGraphs
                .FirstOrDefaultAsync(x => 
                    x.ChatId == chatId && 
                    x.ItemType == "relation" && 
                    x.FromEntity == relation.From && 
                    x.ToEntity == relation.To && 
                    x.RelationType == relation.RelationType);

            if (existing == null)
            {
                _dbContext.MemoryGraphs.Add(new Model.Data.MemoryGraph
                {
                    ChatId = chatId,
                    FromEntity = relation.From,
                    ToEntity = relation.To,
                    RelationType = relation.RelationType,
                    ItemType = "relation",
                    CreatedTime = DateTime.UtcNow
                });
            }
        }

        public async Task<List<Entity>> CreateEntitiesAsync(List<Entity> entities, long chatId)
        {
            var newEntities = new List<Entity>();
            
            foreach (var entity in entities)
            {
                if (!await _dbContext.MemoryGraphs.AnyAsync(x => 
                    x.ChatId == chatId && 
                    x.ItemType == "entity" && 
                    x.Name == entity.Name))
                {
                    await SaveEntityAsync(chatId, entity);
                    newEntities.Add(entity);
                }
            }
            
            await _dbContext.SaveChangesAsync();
            return newEntities;
        }

        public async Task<List<Relation>> CreateRelationsAsync(List<Relation> relations, long chatId)
        {
            var newRelations = new List<Relation>();
            
            foreach (var relation in relations)
            {
                if (!await _dbContext.MemoryGraphs.AnyAsync(x => 
                    x.ChatId == chatId && 
                    x.ItemType == "relation" && 
                    x.FromEntity == relation.From && 
                    x.ToEntity == relation.To && 
                    x.RelationType == relation.RelationType))
                {
                    await SaveRelationAsync(chatId, relation);
                    newRelations.Add(relation);
                }
            }
            
            await _dbContext.SaveChangesAsync();
            return newRelations;
        }

        public async Task<List<ObservationResult>> AddObservationsAsync(List<ObservationInput> observations, long chatId)
        {
            var results = new List<ObservationResult>();

            foreach (var obs in observations)
            {
                var entity = await _dbContext.MemoryGraphs
                    .FirstOrDefaultAsync(x => 
                        x.ChatId == chatId && 
                        x.ItemType == "entity" && 
                        x.Name == obs.EntityName);

                if (entity == null)
                {
                    throw new Exception($"Entity {obs.EntityName} not found");
                }

                var existingObservations = entity.Observations?.Split('|').ToList() ?? new List<string>();
                var newObservations = obs.Contents
                    .Where(c => !existingObservations.Contains(c))
                    .ToList();

                entity.Observations = string.Join("|", existingObservations.Concat(newObservations));
                results.Add(new ObservationResult
                {
                    EntityName = obs.EntityName,
                    AddedObservations = newObservations
                });
            }

            await _dbContext.SaveChangesAsync();
            return results;
        }

        public async Task DeleteEntitiesAsync(List<string> entityNames, long chatId)
        {
            // Delete entities
            var entitiesToDelete = await _dbContext.MemoryGraphs
                .Where(x => x.ChatId == chatId && 
                           x.ItemType == "entity" && 
                           entityNames.Contains(x.Name))
                .ToListAsync();
            
            _dbContext.MemoryGraphs.RemoveRange(entitiesToDelete);

            // Delete related relations
            var relationsToDelete = await _dbContext.MemoryGraphs
                .Where(x => x.ChatId == chatId && 
                           x.ItemType == "relation" && 
                           (entityNames.Contains(x.FromEntity) || entityNames.Contains(x.ToEntity)))
                .ToListAsync();
            
            _dbContext.MemoryGraphs.RemoveRange(relationsToDelete);

            await _dbContext.SaveChangesAsync();
        }

        public async Task DeleteObservationsAsync(List<ObservationDeletion> deletions, long chatId)
        {
            foreach (var del in deletions)
            {
                var entity = await _dbContext.MemoryGraphs
                    .FirstOrDefaultAsync(x => 
                        x.ChatId == chatId && 
                        x.ItemType == "entity" && 
                        x.Name == del.EntityName);

                if (entity != null)
                {
                    var existingObservations = entity.Observations?.Split('|').ToList() ?? new List<string>();
                    entity.Observations = string.Join("|", 
                        existingObservations.Where(o => !del.Observations.Contains(o)));
                }
            }

            await _dbContext.SaveChangesAsync();
        }

        public async Task DeleteRelationsAsync(List<Relation> relations, long chatId)
        {
            var relationsToDelete = await _dbContext.MemoryGraphs
                .Where(x => x.ChatId == chatId && 
                           x.ItemType == "relation" &&
                           relations.Any(del =>
                               del.From == x.FromEntity &&
                               del.To == x.ToEntity &&
                               del.RelationType == x.RelationType))
                .ToListAsync();
            
            _dbContext.MemoryGraphs.RemoveRange(relationsToDelete);
            await _dbContext.SaveChangesAsync();
        }

        public async Task<KnowledgeGraph> ReadGraphAsync(long chatId)
        {
            return await LoadGraphAsync(chatId);
        }

        public async Task<KnowledgeGraph> SearchNodesAsync(string query, long chatId)
        {
            var graph = await LoadGraphAsync(chatId);
            var lowerQuery = query.ToLower();

            var filteredEntities = graph.Entities.Where(e => 
                e.Name.ToLower().Contains(lowerQuery) ||
                e.EntityType.ToLower().Contains(lowerQuery) ||
                e.Observations.Any(o => o.ToLower().Contains(lowerQuery))).ToList();

            var filteredEntityNames = filteredEntities.Select(e => e.Name).ToHashSet();
            var filteredRelations = graph.Relations.Where(r => 
                filteredEntityNames.Contains(r.From) && 
                filteredEntityNames.Contains(r.To)).ToList();

            return new KnowledgeGraph
            {
                Entities = filteredEntities,
                Relations = filteredRelations
            };
        }

        public async Task<KnowledgeGraph> OpenNodesAsync(List<string> names, long chatId)
        {
            var graph = await LoadGraphAsync(chatId);
            var filteredEntities = graph.Entities.Where(e => 
                names.Contains(e.Name)).ToList();

            var filteredEntityNames = filteredEntities.Select(e => e.Name).ToHashSet();
            var filteredRelations = graph.Relations.Where(r => 
                filteredEntityNames.Contains(r.From) && 
                filteredEntityNames.Contains(r.To)).ToList();

            return new KnowledgeGraph
            {
                Entities = filteredEntities,
                Relations = filteredRelations
            };
        }
    }

    public class ObservationInput
    {
        public string EntityName { get; set; }
        public List<string> Contents { get; set; }
    }

    public class ObservationDeletion
    {
        public string EntityName { get; set; }
        public List<string> Observations { get; set; }
    }

    public class ObservationResult
    {
        public string EntityName { get; set; }
        public List<string> AddedObservations { get; set; }
    }

    public class SearchQuery
    {
        public string Query { get; set; }
    }
}
