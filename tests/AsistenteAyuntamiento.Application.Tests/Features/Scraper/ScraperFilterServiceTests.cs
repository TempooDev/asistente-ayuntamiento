using AsistenteAyuntamiento.Application.Common.Interfaces;
using AsistenteAyuntamiento.Application.Features.Scraper;
using AsistenteAyuntamiento.Application.Features.Scraper.DTOs;
using AsistenteAyuntamiento.Domain.Features.Scraper;
using Microsoft.EntityFrameworkCore;
using Moq;
using MockQueryable.Moq;
using Xunit;

namespace AsistenteAyuntamiento.Application.Tests.Features.Scraper;

public class ScraperFilterServiceTests
{
    private Mock<IAppDbContext> _dbContextMock;
    private Mock<DbSet<ScraperFilterRule>> _dbSetMock;
    private ScraperFilterService _service;

    public ScraperFilterServiceTests()
    {
        _dbContextMock = new Mock<IAppDbContext>();
        _dbSetMock = new List<ScraperFilterRule>().BuildMockDbSet();
        _dbContextMock.Setup(db => db.ScraperFilterRules).Returns(_dbSetMock.Object);
        _service = new ScraperFilterService(_dbContextMock.Object);
    }

    [Fact]
    public async Task CreateRule_ShouldAddRuleToDatabase()
    {
        // Arrange
        var dto = new CreateFilterRuleDto { Provider = "BOE", FilterType = "Title", Value = "Test", IsActive = true };

        // Act
        var result = await _service.CreateRuleAsync(dto);

        // Assert
        Assert.Equal("BOE", result.Provider);
        _dbSetMock.Verify(m => m.Add(It.IsAny<ScraperFilterRule>()), Times.Once());
        _dbContextMock.Verify(m => m.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once());
    }

    [Fact]
    public async Task GetActiveRules_ShouldReturnOnlyActiveRules()
    {
        // Arrange
        var rules = new List<ScraperFilterRule>
        {
            new ScraperFilterRule { Id = 1, Provider = "BOE", FilterType = "T", Value = "1", IsActive = true, CreatedAt = DateTime.UtcNow },
            new ScraperFilterRule { Id = 2, Provider = "BOE", FilterType = "T", Value = "2", IsActive = false, CreatedAt = DateTime.UtcNow }
        };
        var mockSet = rules.BuildMockDbSet();
        _dbContextMock.Setup(db => db.ScraperFilterRules).Returns(mockSet.Object);

        // Act
        var activeRules = await _service.GetActiveRulesAsync();

        // Assert
        Assert.Single(activeRules);
        Assert.Equal("1", activeRules[0].Value);
    }
}

