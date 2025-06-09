using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
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
        private readonly ILogger<MemoryService> _logger;

        // JsonSerializer配置
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        public MemoryService(DataDbContext dbContext, ILogger<MemoryService> logger)
        {
            _dbContext = dbContext;
            _logger = logger;
            _graphManager = new KnowledgeGraphManager(_dbContext, _logger);
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
                _logger.LogDebug("ProcessMemoryCommandAsync called with command: {Command}, arguments: {Arguments}, chatId: {ChatId}", 
                    command, arguments, toolContext.ChatId);

                switch (command.ToLower())
                {
                    case "create_entities":
                        var entitiesWrapper = JsonSerializer.Deserialize<EntityWrapper>(arguments, JsonOptions);
                        return await _graphManager.CreateEntitiesAsync(entitiesWrapper.Entities, toolContext.ChatId);
                    case "create_relations":
                        var relationsWrapper = JsonSerializer.Deserialize<RelationWrapper>(arguments, JsonOptions);
                        return await _graphManager.CreateRelationsAsync(relationsWrapper.Relations, toolContext.ChatId);
                    case "add_observations":
                        var obsWrapper = JsonSerializer.Deserialize<ObservationWrapper>(arguments, JsonOptions);
                        return await _graphManager.AddObservationsAsync(obsWrapper.Observations, toolContext.ChatId);
                    case "delete_entities":
                        var entityNamesWrapper = JsonSerializer.Deserialize<EntityNamesWrapper>(arguments, JsonOptions);
                        await _graphManager.DeleteEntitiesAsync(entityNamesWrapper.EntityNames, toolContext.ChatId);
                        return "Entities deleted successfully";
                    case "delete_observations":
                        var obsDeletionsWrapper = JsonSerializer.Deserialize<ObservationDeletionWrapper>(arguments, JsonOptions);
                        await _graphManager.DeleteObservationsAsync(obsDeletionsWrapper.Deletions, toolContext.ChatId);
                        return "Observations deleted successfully";
                    case "delete_relations":
                        var delRelationsWrapper = JsonSerializer.Deserialize<RelationWrapper>(arguments, JsonOptions);
                        await _graphManager.DeleteRelationsAsync(delRelationsWrapper.Relations, toolContext.ChatId);
                        return "Relations deleted successfully";
                    case "read_graph":
                        return await _graphManager.ReadGraphAsync(toolContext.ChatId);
                    case "search_nodes":
                        var searchQuery = JsonSerializer.Deserialize<SearchQuery>(arguments, JsonOptions);
                        if (searchQuery?.Query == null)
                        {
                            throw new ArgumentException("Search query is required");
                        }
                        return await _graphManager.SearchNodesAsync(searchQuery.Query, toolContext.ChatId);
                    case "open_nodes":
                        var nodeNamesWrapper = JsonSerializer.Deserialize<NodeNamesWrapper>(arguments, JsonOptions);
                        return await _graphManager.OpenNodesAsync(nodeNamesWrapper.Names, toolContext.ChatId);
                    default:
                        throw new ArgumentException($"Unknown command: {command}");
                }
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "JSON deserialization failed for command: {Command}, arguments: {Arguments}", command, arguments);
                throw new Exception($"Memory command failed - Invalid JSON format: {ex.Message}", ex);
            }
            catch (ArgumentException ex)
            {
                _logger.LogError(ex, "Invalid argument for command: {Command}, arguments: {Arguments}", command, arguments);
                throw new Exception($"Memory command failed - {ex.Message}", ex);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Memory command failed for command: {Command}, arguments: {Arguments}, chatId: {ChatId}", 
                    command, arguments, toolContext.ChatId);
                throw new Exception($"Memory command failed: {ex.Message}", ex);
            }
        }
    }

    // 包装类用于正确的JSON反序列化
    public class EntityWrapper
    {
        public List<Entity> Entities { get; set; } = new List<Entity>();
    }

    public class RelationWrapper
    {
        public List<Relation> Relations { get; set; } = new List<Relation>();
    }

    public class ObservationWrapper
    {
        public List<ObservationInput> Observations { get; set; } = new List<ObservationInput>();
    }

    public class EntityNamesWrapper
    {
        public List<string> EntityNames { get; set; } = new List<string>();
    }

    public class ObservationDeletionWrapper
    {
        public List<ObservationDeletion> Deletions { get; set; } = new List<ObservationDeletion>();
    }

    public class NodeNamesWrapper
    {
        public List<string> Names { get; set; } = new List<string>();
    }

    [Injectable(Microsoft.Extensions.DependencyInjection.ServiceLifetime.Transient)]
    public class KnowledgeGraphManager : IService
    {
        public string ServiceName => "KnowledgeGraphManager";
        
        private readonly DataDbContext _dbContext;
        private readonly ILogger<MemoryService> _logger;

        // 使用更安全的分隔符，避免冲突
        private const string ObservationSeparator = "|||";

        public KnowledgeGraphManager(DataDbContext dbContext, ILogger<MemoryService> logger)
        {
            _dbContext = dbContext;
            _logger = logger;
        }

        private async Task<KnowledgeGraph> LoadGraphAsync(long chatId)
        {
            try
            {
                _logger.LogDebug("Loading knowledge graph for chatId: {ChatId}", chatId);
                
                var graph = new KnowledgeGraph();
                
                var dbEntities = await _dbContext.MemoryGraphs
                    .Where(x => x.ChatId == chatId && x.ItemType == "entity")
                    .ToListAsync();

                var dbRelations = await _dbContext.MemoryGraphs
                    .Where(x => x.ChatId == chatId && x.ItemType == "relation")
                    .ToListAsync();

                graph.Entities = dbEntities.Select(e => new Entity
                {
                    Name = e.Name ?? "",
                    EntityType = e.EntityType ?? "",
                    Observations = ParseObservations(e.Observations)
                }).ToList();

                graph.Relations = dbRelations.Select(r => new Relation
                {
                    From = r.FromEntity ?? "",
                    To = r.ToEntity ?? "",
                    RelationType = r.RelationType ?? ""
                }).ToList();

                _logger.LogDebug("Loaded {EntityCount} entities and {RelationCount} relations for chatId: {ChatId}", 
                    graph.Entities.Count, graph.Relations.Count, chatId);

                return graph;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load knowledge graph for chatId: {ChatId}", chatId);
                throw;
            }
        }

        private List<string> ParseObservations(string observations)
        {
            if (string.IsNullOrEmpty(observations))
                return new List<string>();
                
            return observations.Split(new[] { ObservationSeparator }, StringSplitOptions.RemoveEmptyEntries)
                              .Select(o => o.Trim())
                              .Where(o => !string.IsNullOrEmpty(o))
                              .ToList();
        }

        private string SerializeObservations(List<string> observations)
        {
            if (observations == null || !observations.Any())
                return null;
                
            return string.Join(ObservationSeparator, observations.Where(o => !string.IsNullOrEmpty(o)));
        }

        private async Task SaveEntityAsync(long chatId, Entity entity)
        {
            try
            {
                if (string.IsNullOrEmpty(entity.Name))
                {
                    throw new ArgumentException("Entity name cannot be null or empty");
                }

                var existing = await _dbContext.MemoryGraphs
                    .FirstOrDefaultAsync(x => 
                        x.ChatId == chatId && 
                        x.ItemType == "entity" && 
                        x.Name == entity.Name);

                if (existing == null)
                {
                    _logger.LogDebug("Creating new entity: {EntityName} in chatId: {ChatId}", entity.Name, chatId);
                    
                    _dbContext.MemoryGraphs.Add(new Model.Data.MemoryGraph
                    {
                        ChatId = chatId,
                        Name = entity.Name,
                        EntityType = entity.EntityType ?? "",
                        Observations = SerializeObservations(entity.Observations),
                        ItemType = "entity",
                        CreatedTime = DateTime.UtcNow
                    });
                }
                else
                {
                    _logger.LogDebug("Updating existing entity: {EntityName} in chatId: {ChatId}", entity.Name, chatId);
                    
                    existing.EntityType = entity.EntityType ?? "";
                    existing.Observations = SerializeObservations(entity.Observations);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save entity: {EntityName} in chatId: {ChatId}", entity?.Name, chatId);
                throw;
            }
        }

        private async Task SaveRelationAsync(long chatId, Relation relation)
        {
            try
            {
                if (string.IsNullOrEmpty(relation.From) || string.IsNullOrEmpty(relation.To))
                {
                    throw new ArgumentException("Relation From and To cannot be null or empty");
                }

                var existing = await _dbContext.MemoryGraphs
                    .FirstOrDefaultAsync(x => 
                        x.ChatId == chatId && 
                        x.ItemType == "relation" && 
                        x.FromEntity == relation.From && 
                        x.ToEntity == relation.To && 
                        x.RelationType == relation.RelationType);

                if (existing == null)
                {
                    _logger.LogDebug("Creating new relation: {From} -> {To} ({RelationType}) in chatId: {ChatId}", 
                        relation.From, relation.To, relation.RelationType, chatId);
                    
                    _dbContext.MemoryGraphs.Add(new Model.Data.MemoryGraph
                    {
                        ChatId = chatId,
                        Name = $"{relation.From}-{relation.RelationType}-{relation.To}",
                        EntityType = "",
                        FromEntity = relation.From,
                        ToEntity = relation.To,
                        RelationType = relation.RelationType ?? "",
                        ItemType = "relation",
                        CreatedTime = DateTime.UtcNow
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save relation: {From} -> {To} in chatId: {ChatId}", 
                    relation?.From, relation?.To, chatId);
                throw;
            }
        }

        public async Task<List<Entity>> CreateEntitiesAsync(List<Entity> entities, long chatId)
        {
            try
            {
                _logger.LogDebug("Creating {Count} entities for chatId: {ChatId}", entities?.Count ?? 0, chatId);
                
                if (entities == null || !entities.Any())
                {
                    return new List<Entity>();
                }

                var newEntities = new List<Entity>();
                
                foreach (var entity in entities)
                {
                    if (string.IsNullOrEmpty(entity.Name))
                    {
                        _logger.LogWarning("Skipping entity with empty name in chatId: {ChatId}", chatId);
                        continue;
                    }

                    if (!await _dbContext.MemoryGraphs.AnyAsync(x => 
                        x.ChatId == chatId && 
                        x.ItemType == "entity" && 
                        x.Name == entity.Name))
                    {
                        await SaveEntityAsync(chatId, entity);
                        newEntities.Add(entity);
                    }
                    else
                    {
                        _logger.LogDebug("Entity {EntityName} already exists in chatId: {ChatId}", entity.Name, chatId);
                    }
                }
                
                await _dbContext.SaveChangesAsync();
                _logger.LogInformation("Successfully created {Count} new entities for chatId: {ChatId}", newEntities.Count, chatId);
                return newEntities;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create entities for chatId: {ChatId}", chatId);
                throw;
            }
        }

        public async Task<List<Relation>> CreateRelationsAsync(List<Relation> relations, long chatId)
        {
            try
            {
                _logger.LogDebug("Creating {Count} relations for chatId: {ChatId}", relations?.Count ?? 0, chatId);
                
                if (relations == null || !relations.Any())
                {
                    return new List<Relation>();
                }

                var newRelations = new List<Relation>();
                
                foreach (var relation in relations)
                {
                    if (string.IsNullOrEmpty(relation.From) || string.IsNullOrEmpty(relation.To))
                    {
                        _logger.LogWarning("Skipping relation with empty From/To in chatId: {ChatId}", chatId);
                        continue;
                    }

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
                    else
                    {
                        _logger.LogDebug("Relation {From} -> {To} already exists in chatId: {ChatId}", 
                            relation.From, relation.To, chatId);
                    }
                }
                
                await _dbContext.SaveChangesAsync();
                _logger.LogInformation("Successfully created {Count} new relations for chatId: {ChatId}", newRelations.Count, chatId);
                return newRelations;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create relations for chatId: {ChatId}", chatId);
                throw;
            }
        }

        public async Task<List<ObservationResult>> AddObservationsAsync(List<ObservationInput> observations, long chatId)
        {
            try
            {
                _logger.LogDebug("Adding observations for {Count} entities in chatId: {ChatId}", observations?.Count ?? 0, chatId);
                
                if (observations == null || !observations.Any())
                {
                    return new List<ObservationResult>();
                }

                var results = new List<ObservationResult>();

                foreach (var obs in observations)
                {
                    if (string.IsNullOrEmpty(obs.EntityName))
                    {
                        _logger.LogWarning("Skipping observation with empty entity name in chatId: {ChatId}", chatId);
                        continue;
                    }

                    var entity = await _dbContext.MemoryGraphs
                        .FirstOrDefaultAsync(x => 
                            x.ChatId == chatId && 
                            x.ItemType == "entity" && 
                            x.Name == obs.EntityName);

                    if (entity == null)
                    {
                        _logger.LogWarning("Entity {EntityName} not found in chatId: {ChatId}", obs.EntityName, chatId);
                        continue;
                    }

                    var existingObservations = ParseObservations(entity.Observations);
                    var newObservations = obs.Contents
                        ?.Where(c => !string.IsNullOrEmpty(c) && !existingObservations.Contains(c))
                        ?.ToList() ?? new List<string>();

                    if (newObservations.Any())
                    {
                        existingObservations.AddRange(newObservations);
                        entity.Observations = SerializeObservations(existingObservations);
                        
                        results.Add(new ObservationResult
                        {
                            EntityName = obs.EntityName,
                            AddedObservations = newObservations
                        });
                        
                        _logger.LogDebug("Added {Count} new observations to entity {EntityName} in chatId: {ChatId}", 
                            newObservations.Count, obs.EntityName, chatId);
                    }
                }

                await _dbContext.SaveChangesAsync();
                _logger.LogInformation("Successfully added observations for {Count} entities in chatId: {ChatId}", results.Count, chatId);
                return results;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to add observations for chatId: {ChatId}", chatId);
                throw;
            }
        }

        public async Task DeleteEntitiesAsync(List<string> entityNames, long chatId)
        {
            try
            {
                _logger.LogDebug("Deleting {Count} entities in chatId: {ChatId}", entityNames?.Count ?? 0, chatId);
                
                if (entityNames == null || !entityNames.Any())
                {
                    return;
                }

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
                _logger.LogInformation("Successfully deleted {EntityCount} entities and {RelationCount} related relations for chatId: {ChatId}", 
                    entitiesToDelete.Count, relationsToDelete.Count, chatId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete entities for chatId: {ChatId}", chatId);
                throw;
            }
        }

        public async Task DeleteObservationsAsync(List<ObservationDeletion> deletions, long chatId)
        {
            try
            {
                _logger.LogDebug("Deleting observations for {Count} entities in chatId: {ChatId}", deletions?.Count ?? 0, chatId);
                
                if (deletions == null || !deletions.Any())
                {
                    return;
                }

                foreach (var del in deletions)
                {
                    if (string.IsNullOrEmpty(del.EntityName))
                    {
                        continue;
                    }

                    var entity = await _dbContext.MemoryGraphs
                        .FirstOrDefaultAsync(x => 
                            x.ChatId == chatId && 
                            x.ItemType == "entity" && 
                            x.Name == del.EntityName);

                    if (entity != null)
                    {
                        var existingObservations = ParseObservations(entity.Observations);
                        var filteredObservations = existingObservations
                            .Where(o => !del.Observations.Contains(o))
                            .ToList();
                            
                        entity.Observations = SerializeObservations(filteredObservations);
                        
                        _logger.LogDebug("Removed {Count} observations from entity {EntityName} in chatId: {ChatId}", 
                            existingObservations.Count - filteredObservations.Count, del.EntityName, chatId);
                    }
                }

                await _dbContext.SaveChangesAsync();
                _logger.LogInformation("Successfully deleted observations for chatId: {ChatId}", chatId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete observations for chatId: {ChatId}", chatId);
                throw;
            }
        }

        public async Task DeleteRelationsAsync(List<Relation> relations, long chatId)
        {
            try
            {
                _logger.LogDebug("Deleting {Count} relations in chatId: {ChatId}", relations?.Count ?? 0, chatId);
                
                if (relations == null || !relations.Any())
                {
                    return;
                }

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
                
                _logger.LogInformation("Successfully deleted {Count} relations for chatId: {ChatId}", relationsToDelete.Count, chatId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete relations for chatId: {ChatId}", chatId);
                throw;
            }
        }

        public async Task<KnowledgeGraph> ReadGraphAsync(long chatId)
        {
            return await LoadGraphAsync(chatId);
        }

        public async Task<KnowledgeGraph> SearchNodesAsync(string query, long chatId)
        {
            try
            {
                _logger.LogDebug("Searching nodes with query: {Query} in chatId: {ChatId}", query, chatId);
                
                var graph = await LoadGraphAsync(chatId);
                
                if (string.IsNullOrEmpty(query))
                {
                    return graph;
                }
                
                var lowerQuery = query.ToLower();

                var filteredEntities = graph.Entities.Where(e => 
                    e.Name.ToLower().Contains(lowerQuery) ||
                    e.EntityType.ToLower().Contains(lowerQuery) ||
                    e.Observations.Any(o => o.ToLower().Contains(lowerQuery))).ToList();

                var filteredEntityNames = filteredEntities.Select(e => e.Name).ToHashSet();
                var filteredRelations = graph.Relations.Where(r => 
                    filteredEntityNames.Contains(r.From) && 
                    filteredEntityNames.Contains(r.To)).ToList();

                _logger.LogDebug("Search found {EntityCount} entities and {RelationCount} relations for query: {Query} in chatId: {ChatId}", 
                    filteredEntities.Count, filteredRelations.Count, query, chatId);

                return new KnowledgeGraph
                {
                    Entities = filteredEntities,
                    Relations = filteredRelations
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to search nodes for query: {Query} in chatId: {ChatId}", query, chatId);
                throw;
            }
        }

        public async Task<KnowledgeGraph> OpenNodesAsync(List<string> names, long chatId)
        {
            try
            {
                _logger.LogDebug("Opening {Count} nodes in chatId: {ChatId}", names?.Count ?? 0, chatId);
                
                if (names == null || !names.Any())
                {
                    return new KnowledgeGraph();
                }

                var graph = await LoadGraphAsync(chatId);
                var filteredEntities = graph.Entities.Where(e => 
                    names.Contains(e.Name)).ToList();

                var filteredEntityNames = filteredEntities.Select(e => e.Name).ToHashSet();
                var filteredRelations = graph.Relations.Where(r => 
                    filteredEntityNames.Contains(r.From) && 
                    filteredEntityNames.Contains(r.To)).ToList();

                _logger.LogDebug("Opened {EntityCount} entities and {RelationCount} relations for chatId: {ChatId}", 
                    filteredEntities.Count, filteredRelations.Count, chatId);

                return new KnowledgeGraph
                {
                    Entities = filteredEntities,
                    Relations = filteredRelations
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to open nodes for chatId: {ChatId}", chatId);
                throw;
            }
        }
    }

    public class ObservationInput
    {
        public string EntityName { get; set; }
        public List<string> Contents { get; set; } = new List<string>();
    }

    public class ObservationDeletion
    {
        public string EntityName { get; set; }
        public List<string> Observations { get; set; } = new List<string>();
    }

    public class ObservationResult
    {
        public string EntityName { get; set; }
        public List<string> AddedObservations { get; set; } = new List<string>();
    }

    public class SearchQuery
    {
        public string Query { get; set; }
    }
}
