using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using Moq;
using Notesnook.API.Controllers;
using Notesnook.API.Interfaces;
using Notesnook.API.Models;
using Notesnook.API.Tests.Helpers;
using Notesnook.API.Tests.TestData;

namespace Notesnook.API.Tests.Controllers
{
    [TestClass]
    public class MonographsControllerTests
    {
        private Mock<IMonographRepository> _mockMonographRepository;
        private Mock<ISyncDeviceServiceWrapper> _mockSyncDeviceServiceWrapper;
        private Mock<IMessengerService> _mockMessengerService;
        private MonographsController _controller;

        [TestInitialize]
        public void TestInitialize()
        {
            _mockMonographRepository = new Mock<IMonographRepository>();
            _mockSyncDeviceServiceWrapper = new Mock<ISyncDeviceServiceWrapper>();
            _mockMessengerService = new Mock<IMessengerService>();

            _controller = new MonographsController(
                _mockMonographRepository.Object,
                _mockSyncDeviceServiceWrapper.Object,
                _mockMessengerService.Object);

            ControllerTestHelper.SetupControllerContext(
                _controller,
                MonographTestData.TestUserId,
                MonographTestData.TestJtiToken);
        }

        [TestCategory("PublishAsync")]
        [TestMethod]
        public async Task PublishAsync_ValidMonograph_PublishesSuccessfully()
        {
            var monograph = MonographTestData.CreateMonograph();
            var deviceId = MonographTestData.TestDeviceId;
            _mockMonographRepository
                .Setup(x => x.FindByUserAndItemAsync(MonographTestData.TestUserId, It.IsAny<Monograph>()))
                .ReturnsAsync((Monograph?)null);
            var mockReplaceResult = new Mock<ReplaceOneResult>();
            mockReplaceResult.Setup(x => x.IsAcknowledged).Returns(true);
            _mockMonographRepository
                .Setup(x => x.PublishOrUpdateAsync(MonographTestData.TestUserId, It.IsAny<Monograph>()))
                .ReturnsAsync(mockReplaceResult.Object);

            var result = await _controller.PublishAsync(deviceId, monograph);

            Assert.IsInstanceOfType(result, typeof(OkObjectResult));
            Assert.IsNotNull(((OkObjectResult)result).Value);
            _mockMonographRepository.Verify(
                x => x.PublishOrUpdateAsync(MonographTestData.TestUserId, It.Is<Monograph>(m =>
                    m.UserId == MonographTestData.TestUserId &&
                    m.Title == monograph.Title &&
                    m.Content == monograph.Content &&
                    m.Password == monograph.Password &&
                    m.SelfDestruct == monograph.SelfDestruct &&
                    m.CompressedContent != null &&
                    m.EncryptedContent == null &&
                    m.DatePublished > 0
                )),
                Times.Once);
            _mockSyncDeviceServiceWrapper.Verify(
                x => x.MarkMonographForSyncAsync(MonographTestData.TestUserId, monograph.ItemId, deviceId),
                Times.Once);
            _mockMessengerService.Verify(
                x => x.SendTriggerSyncEventAsync("Monographs updated", MonographTestData.TestUserId, MonographTestData.TestJtiToken, false),
                Times.Once);
        }

        [TestCategory("PublishAsync")]
        [TestMethod]
        public async Task PublishAsync_NoUserId_ReturnsUnauthorized()
        {
            ControllerTestHelper.SetupUnauthenticatedControllerContext(_controller);
            var monograph = MonographTestData.CreateMonograph();
            var deviceId = MonographTestData.TestDeviceId;

            var result = await _controller.PublishAsync(deviceId, monograph);

            Assert.IsInstanceOfType(result, typeof(UnauthorizedResult));
            _mockMonographRepository.Verify(
                x => x.PublishOrUpdateAsync(It.IsAny<string>(), It.IsAny<Monograph>()),
                Times.Never);
            _mockSyncDeviceServiceWrapper.Verify(
                x => x.MarkMonographForSyncAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
                Times.Never);
            _mockMessengerService.Verify(
                x => x.SendTriggerSyncEventAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()),
                Times.Never);
        }

        [TestCategory("PublishAsync")]
        [TestMethod]
        public async Task PublishAsync_MonographAlreadyExists_ReturnsConflict()
        {
            var monograph = MonographTestData.CreateMonograph(isExisting: true);
            var deviceId = MonographTestData.TestDeviceId;
            _mockMonographRepository
                .Setup(x => x.FindByUserAndItemAsync(MonographTestData.TestUserId, It.IsAny<Monograph>()))
                .ReturnsAsync(monograph);

            var result = await _controller.PublishAsync(deviceId, monograph);

            Assert.IsInstanceOfType(result, typeof(ConflictObjectResult));
            var conflictResult = (ConflictObjectResult)result;
            Assert.AreEqual("This monograph is already published.", conflictResult.Value);
            _mockMonographRepository.Verify(
                x => x.PublishOrUpdateAsync(It.IsAny<string>(), It.IsAny<Monograph>()),
                Times.Never);
            _mockSyncDeviceServiceWrapper.Verify(
                x => x.MarkMonographForSyncAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
                Times.Never);
            _mockMessengerService.Verify(
                x => x.SendTriggerSyncEventAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()),
                Times.Never);
        }

        [TestCategory("PublishAsync")]
        [TestMethod]
        public async Task PublishAsync_ExistingMonographSoftDeleted_PublishesSuccessfully()
        {
            var monograph = MonographTestData.CreateMonograph(isExisting: true, deleted: true);
            var deviceId = MonographTestData.TestDeviceId;
            _mockMonographRepository
                .Setup(x => x.FindByUserAndItemAsync(MonographTestData.TestUserId, It.IsAny<Monograph>()))
                .ReturnsAsync(monograph);
            var mockReplaceResult = new Mock<ReplaceOneResult>();
            mockReplaceResult.Setup(x => x.IsAcknowledged).Returns(true);
            _mockMonographRepository
                .Setup(x => x.PublishOrUpdateAsync(MonographTestData.TestUserId, It.IsAny<Monograph>()))
                .ReturnsAsync(mockReplaceResult.Object);

            var result = await _controller.PublishAsync(deviceId, monograph);

            Assert.IsInstanceOfType(result, typeof(OkObjectResult));
            Assert.IsNotNull(((OkObjectResult)result).Value);
            _mockMonographRepository.Verify(
                x => x.PublishOrUpdateAsync(MonographTestData.TestUserId, It.Is<Monograph>(m =>
                    m.UserId == MonographTestData.TestUserId &&
                    m.Title == monograph.Title &&
                    m.Content == monograph.Content &&
                    m.Password == monograph.Password &&
                    m.SelfDestruct == monograph.SelfDestruct &&
                    m.CompressedContent != null &&
                    m.EncryptedContent == null &&
                    m.DatePublished > 0
                )),
                Times.Once);
            _mockSyncDeviceServiceWrapper.Verify(
                x => x.MarkMonographForSyncAsync(MonographTestData.TestUserId, monograph.ItemId, deviceId),
                Times.Once);
            _mockMessengerService.Verify(
                x => x.SendTriggerSyncEventAsync("Monographs updated", MonographTestData.TestUserId, MonographTestData.TestJtiToken, false),
                Times.Once);
        }

        [TestCategory("PublishAsync")]
        [TestMethod]
        public async Task PublishAsync_EncryptedMonographTooLarge_ReturnsBadRequest()
        {
            var monograph = MonographTestData.CreateLargeEncryptedMonograph();
            var deviceId = MonographTestData.TestDeviceId;
            _mockMonographRepository
                .Setup(x => x.FindByUserAndItemAsync(MonographTestData.TestUserId, It.IsAny<Monograph>()))
                .ReturnsAsync((Monograph?)null);

            var result = await _controller.PublishAsync(deviceId, monograph);

            Assert.IsInstanceOfType(result, typeof(BadRequestObjectResult));
            var badRequestResult = (BadRequestObjectResult)result;
            Assert.AreEqual("Monograph is too big. Max allowed size is 15mb.", badRequestResult.Value);
            _mockMonographRepository.Verify(
                x => x.PublishOrUpdateAsync(It.IsAny<string>(), It.IsAny<Monograph>()),
                Times.Never);
            _mockSyncDeviceServiceWrapper.Verify(
                x => x.MarkMonographForSyncAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
                Times.Never);
            _mockMessengerService.Verify(
                x => x.SendTriggerSyncEventAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()),
                Times.Never);
        }

        [TestCategory("PublishAsync")]
        [TestMethod]
        public async Task PublishAsync_WithEncryptedContent_PublishesSuccessfully()
        {
            var monograph = MonographTestData.CreateEncryptedMonograph();
            var deviceId = MonographTestData.TestDeviceId;
            _mockMonographRepository
                .Setup(x => x.FindByUserAndItemAsync(MonographTestData.TestUserId, It.IsAny<Monograph>()))
                .ReturnsAsync((Monograph?)null);
            var mockReplaceResult = new Mock<ReplaceOneResult>();
            mockReplaceResult.Setup(x => x.IsAcknowledged).Returns(true);

            _mockMonographRepository
                .Setup(x => x.PublishOrUpdateAsync(MonographTestData.TestUserId, It.IsAny<Monograph>()))
                .ReturnsAsync(mockReplaceResult.Object);

            var result = await _controller.PublishAsync(deviceId, monograph);

            Assert.IsInstanceOfType(result, typeof(OkObjectResult));
            Assert.IsNotNull(((OkObjectResult)result).Value);
            _mockMonographRepository.Verify(
                x => x.PublishOrUpdateAsync(MonographTestData.TestUserId, It.Is<Monograph>(m =>
                    m.UserId == MonographTestData.TestUserId &&
                    m.Title == monograph.Title &&
                    m.Content == monograph.Content &&
                    m.Password == monograph.Password &&
                    m.SelfDestruct == monograph.SelfDestruct &&
                    m.CompressedContent == null &&
                    m.EncryptedContent != null &&
                    m.DatePublished > 0
                )),
                Times.Once);
            _mockSyncDeviceServiceWrapper.Verify(
                x => x.MarkMonographForSyncAsync(MonographTestData.TestUserId, monograph.ItemId, deviceId),
                Times.Once);
            _mockMessengerService.Verify(
                x => x.SendTriggerSyncEventAsync("Monographs updated", MonographTestData.TestUserId, MonographTestData.TestJtiToken, false),
                Times.Once);
        }

        [TestCategory("PublishAsync")]
        [TestMethod]
        public async Task PublishAsync_WithNullDeviceId_SkipsSyncOperations()
        {
            var monograph = MonographTestData.CreateMonograph();
            string? deviceId = null;
            _mockMonographRepository
                .Setup(x => x.FindByUserAndItemAsync(MonographTestData.TestUserId, It.IsAny<Monograph>()))
                .ReturnsAsync((Monograph?)null);
            var mockReplaceResult = new Mock<ReplaceOneResult>();
            mockReplaceResult.Setup(x => x.IsAcknowledged).Returns(true);
            _mockMonographRepository
                .Setup(x => x.PublishOrUpdateAsync(MonographTestData.TestUserId, It.IsAny<Monograph>()))
                .ReturnsAsync(mockReplaceResult.Object);

            var result = await _controller.PublishAsync(deviceId, monograph);

            Assert.IsInstanceOfType(result, typeof(OkObjectResult));
            Assert.IsNotNull(((OkObjectResult)result).Value);
            _mockMonographRepository.Verify(
                x => x.PublishOrUpdateAsync(MonographTestData.TestUserId, It.Is<Monograph>(m =>
                    m.UserId == MonographTestData.TestUserId &&
                    m.Title == monograph.Title &&
                    m.Content == monograph.Content &&
                    m.Password == monograph.Password &&
                    m.SelfDestruct == monograph.SelfDestruct &&
                    m.CompressedContent != null &&
                    m.EncryptedContent == null &&
                    m.DatePublished > 0
                )),
                Times.Once);
            _mockSyncDeviceServiceWrapper.Verify(
                x => x.MarkMonographForSyncAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
                Times.Never);
            _mockMessengerService.Verify(
                x => x.SendTriggerSyncEventAsync("Monographs updated", MonographTestData.TestUserId, MonographTestData.TestJtiToken, false),
                Times.Never);
        }

        [TestCategory("UpdateAsync")]
        [TestMethod]
        public async Task UpdateAsync_ValidMonograph_UpdatesSuccessfully()
        {
            var monograph = MonographTestData.CreateMonograph(isExisting: true);
            var deviceId = MonographTestData.TestDeviceId;
            _mockMonographRepository
                .Setup(x => x.FindByUserAndItemAsync(MonographTestData.TestUserId, It.IsAny<Monograph>()))
                .ReturnsAsync(monograph);
            var mockUpdateResult = new Mock<UpdateResult>();
            mockUpdateResult.Setup(x => x.IsAcknowledged).Returns(true);
            _mockMonographRepository
                .Setup(x => x.UpdateMonographAsync(MonographTestData.TestUserId, It.IsAny<Monograph>()))
                .ReturnsAsync(mockUpdateResult.Object);

            var result = await _controller.UpdateAsync(deviceId, monograph);

            Assert.IsInstanceOfType(result, typeof(OkObjectResult));
            Assert.IsNotNull(((OkObjectResult)result).Value);
            _mockMonographRepository.Verify(
                x => x.UpdateMonographAsync(MonographTestData.TestUserId, It.Is<Monograph>(m =>
                    m.UserId == MonographTestData.TestUserId &&
                    m.Title == monograph.Title &&
                    m.Content == monograph.Content &&
                    m.Password == monograph.Password &&
                    m.SelfDestruct == monograph.SelfDestruct &&
                    m.CompressedContent != null &&
                    m.EncryptedContent == null &&
                    m.DatePublished > 0
                )),
                Times.Once);
            _mockSyncDeviceServiceWrapper.Verify(
                x => x.MarkMonographForSyncAsync(MonographTestData.TestUserId, monograph.ItemId, deviceId),
                Times.Once);
            _mockMessengerService.Verify(
                x => x.SendTriggerSyncEventAsync("Monographs updated", MonographTestData.TestUserId, MonographTestData.TestJtiToken, false),
                Times.Once);
        }

        [TestCategory("UpdateAsync")]
        [TestMethod]
        public async Task UpdateAsync_NoUserId_ReturnsUnauthorized()
        {
            ControllerTestHelper.SetupUnauthenticatedControllerContext(_controller);
            var monograph = MonographTestData.CreateMonograph();
            var deviceId = MonographTestData.TestDeviceId;

            var result = await _controller.UpdateAsync(deviceId, monograph);

            Assert.IsInstanceOfType(result, typeof(UnauthorizedResult));
            _mockMonographRepository.Verify(
                x => x.FindByUserAndItemAsync(It.IsAny<string>(), It.IsAny<Monograph>()),
                Times.Never);
            _mockMonographRepository.Verify(
                x => x.UpdateMonographAsync(It.IsAny<string>(), It.IsAny<Monograph>()),
                Times.Never);
            _mockSyncDeviceServiceWrapper.Verify(
                x => x.MarkMonographForSyncAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
                Times.Never);
            _mockMessengerService.Verify(
                x => x.SendTriggerSyncEventAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()),
                Times.Never);
        }

        [TestCategory("UpdateAsync")]
        [TestMethod]
        public async Task UpdateAsync_MonographNotFound_ReturnsNotFound()
        {
            var monograph = MonographTestData.CreateMonograph();
            var deviceId = MonographTestData.TestDeviceId;
            _mockMonographRepository
                .Setup(x => x.FindByUserAndItemAsync(MonographTestData.TestUserId, It.IsAny<Monograph>()))
                .ReturnsAsync((Monograph?)null);

            var result = await _controller.UpdateAsync(deviceId, monograph);

            Assert.IsInstanceOfType(result, typeof(NotFoundResult));
            _mockMonographRepository.Verify(
                x => x.UpdateMonographAsync(It.IsAny<string>(), It.IsAny<Monograph>()),
                Times.Never);
            _mockSyncDeviceServiceWrapper.Verify(
                x => x.MarkMonographForSyncAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
                Times.Never);
            _mockMessengerService.Verify(
                x => x.SendTriggerSyncEventAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()),
                Times.Never);
        }

        [TestCategory("UpdateAsync")]
        [TestMethod]
        public async Task UpdateAsync_MonographDeleted_ReturnsNotFound()
        {
            var monograph = MonographTestData.CreateMonograph(deleted: true, isExisting: true);
            var deviceId = MonographTestData.TestDeviceId;
            _mockMonographRepository
                .Setup(x => x.FindByUserAndItemAsync(MonographTestData.TestUserId, It.IsAny<Monograph>()))
                .ReturnsAsync(monograph);

            var result = await _controller.UpdateAsync(deviceId, monograph);

            Assert.IsInstanceOfType(result, typeof(NotFoundResult));
            _mockMonographRepository.Verify(
                x => x.UpdateMonographAsync(It.IsAny<string>(), It.IsAny<Monograph>()),
                Times.Never);
            _mockSyncDeviceServiceWrapper.Verify(
                x => x.MarkMonographForSyncAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
                Times.Never);
            _mockMessengerService.Verify(
                x => x.SendTriggerSyncEventAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()),
                Times.Never);
        }

        [TestCategory("UpdateAsync")]
        [TestMethod]
        public async Task UpdateAsync_EncryptedMonographTooLarge_ReturnsBadRequest()
        {
            var monograph = MonographTestData.CreateLargeEncryptedMonograph();
            var deviceId = MonographTestData.TestDeviceId;
            _mockMonographRepository
                .Setup(x => x.FindByUserAndItemAsync(MonographTestData.TestUserId, It.IsAny<Monograph>()))
                .ReturnsAsync(monograph);

            var result = await _controller.UpdateAsync(deviceId, monograph);

            Assert.IsInstanceOfType(result, typeof(BadRequestObjectResult));
            var badRequestResult = (BadRequestObjectResult)result;
            Assert.AreEqual("Monograph is too big. Max allowed size is 15mb.", badRequestResult.Value);
            _mockMonographRepository.Verify(
                x => x.UpdateMonographAsync(It.IsAny<string>(), It.IsAny<Monograph>()),
                Times.Never);
            _mockSyncDeviceServiceWrapper.Verify(
                x => x.MarkMonographForSyncAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
                Times.Never);
            _mockMessengerService.Verify(
                x => x.SendTriggerSyncEventAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()),
                Times.Never);
        }

        [TestCategory("UpdateAsync")]
        [TestMethod]
        public async Task UpdateAsync_WithEncryptedContent_UpdatesSuccessfully()
        {
            var monograph = MonographTestData.CreateEncryptedMonograph();
            var deviceId = MonographTestData.TestDeviceId;

            _mockMonographRepository
                .Setup(x => x.FindByUserAndItemAsync(MonographTestData.TestUserId, It.IsAny<Monograph>()))
                .ReturnsAsync(monograph);
            var mockUpdateResult = new Mock<UpdateResult>();
            mockUpdateResult.Setup(x => x.IsAcknowledged).Returns(true);
            _mockMonographRepository
                .Setup(x => x.UpdateMonographAsync(MonographTestData.TestUserId, It.IsAny<Monograph>()))
                .ReturnsAsync(mockUpdateResult.Object);

            var result = await _controller.UpdateAsync(deviceId, monograph);

            Assert.IsInstanceOfType(result, typeof(OkObjectResult));
            Assert.IsNotNull(((OkObjectResult)result).Value);
            _mockMonographRepository.Verify(
                x => x.UpdateMonographAsync(MonographTestData.TestUserId, It.Is<Monograph>(m =>
                    m.UserId == MonographTestData.TestUserId &&
                    m.Title == monograph.Title &&
                    m.Content == monograph.Content &&
                    m.Password == monograph.Password &&
                    m.SelfDestruct == monograph.SelfDestruct &&
                    m.CompressedContent == null &&
                    m.EncryptedContent != null &&
                    m.DatePublished > 0
                )),
                Times.Once);
            _mockSyncDeviceServiceWrapper.Verify(
                x => x.MarkMonographForSyncAsync(MonographTestData.TestUserId, monograph.ItemId, deviceId),
                Times.Once);
            _mockMessengerService.Verify(
                x => x.SendTriggerSyncEventAsync("Monographs updated", MonographTestData.TestUserId, MonographTestData.TestJtiToken, false),
                Times.Once);
        }

        [TestCategory("UpdateAsync")]
        [TestMethod]
        public async Task UpdateAsync_WithNullDeviceId_SkipsSyncOperations()
        {
            var monograph = MonographTestData.CreateMonograph(isExisting: true);
            string? deviceId = null;
            _mockMonographRepository
                .Setup(x => x.FindByUserAndItemAsync(MonographTestData.TestUserId, It.IsAny<Monograph>()))
                .ReturnsAsync(monograph);
            var mockUpdateResult = new Mock<UpdateResult>();
            mockUpdateResult.Setup(x => x.IsAcknowledged).Returns(true);
            _mockMonographRepository
                .Setup(x => x.UpdateMonographAsync(MonographTestData.TestUserId, It.IsAny<Monograph>()))
                .ReturnsAsync(mockUpdateResult.Object);

            var result = await _controller.UpdateAsync(deviceId, monograph);

            Assert.IsInstanceOfType(result, typeof(OkObjectResult));
            Assert.IsNotNull(((OkObjectResult)result).Value);
            _mockMonographRepository.Verify(
                x => x.UpdateMonographAsync(MonographTestData.TestUserId, It.Is<Monograph>(m =>
                    m.UserId == MonographTestData.TestUserId &&
                    m.Title == monograph.Title &&
                    m.Content == monograph.Content &&
                    m.Password == monograph.Password &&
                    m.SelfDestruct == monograph.SelfDestruct &&
                    m.CompressedContent != null &&
                    m.EncryptedContent == null &&
                    m.DatePublished > 0
                )),
                Times.Once);
            _mockSyncDeviceServiceWrapper.Verify(
                x => x.MarkMonographForSyncAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
                Times.Never);
            _mockMessengerService.Verify(
                x => x.SendTriggerSyncEventAsync("Monographs updated", MonographTestData.TestUserId, MonographTestData.TestJtiToken, false),
                Times.Never);
        }

        [TestCategory("GetUserMonographsAsync")]
        [TestMethod]
        public async Task GetUserMonographsAsync_ValidUser_ReturnsMonographIds()
        {
            var ids = MonographTestData.MonographIds();
            _mockMonographRepository
                .Setup(x => x.GetUserMonographIdsAsync(MonographTestData.TestUserId))
                .ReturnsAsync(ids);

            var result = await _controller.GetUserMonographsAsync();

            Assert.IsInstanceOfType(result, typeof(OkObjectResult));
            var okResult = (OkObjectResult)result;
            Assert.IsNotNull(okResult.Value);
            Assert.AreEqual(ids, okResult.Value);
        }

        [TestCategory("GetUserMonographsAsync")]
        [TestMethod]
        public async Task GetUserMonographsAsync_NoUserId_ReturnsUnauthorized()
        {
            ControllerTestHelper.SetupUnauthenticatedControllerContext(_controller);
            var result = await _controller.GetUserMonographsAsync();

            Assert.IsInstanceOfType(result, typeof(UnauthorizedResult));
            _mockMonographRepository.Verify(
                x => x.GetUserMonographIdsAsync(It.IsAny<string>()),
                Times.Never);
        }

        [TestCategory("GetMonographAsync")]
        [TestMethod]
        public async Task GetMonographAsync_ValidId_ReturnsMonograph()
        {
            var monograph = MonographTestData.CreateMonograph(isExisting: true);
            var monographId = monograph.ItemId;
            _mockMonographRepository
                .Setup(x => x.FindByItemIdAsync(monographId))
                .ReturnsAsync(monograph);

            var result = await _controller.GetMonographAsync(monographId);

            Assert.IsInstanceOfType(result, typeof(OkObjectResult));
            var okResult = (OkObjectResult)result;
            Assert.IsNotNull(okResult.Value);
            var returnedMonograph = (Monograph)okResult.Value;
            Assert.AreEqual(monograph.ItemId, returnedMonograph.ItemId);
            Assert.AreEqual(monograph.Title, returnedMonograph.Title);
            Assert.AreEqual(monograph.UserId, returnedMonograph.UserId);
            Assert.IsFalse(returnedMonograph.Deleted);
        }

        [TestCategory("GetMonographAsync")]
        [TestMethod]
        public async Task GetMonographAsync_MonographNotFound_ReturnsNotFound()
        {
            var monographId = "non-existent-id";
            _mockMonographRepository
                .Setup(x => x.FindByItemIdAsync(monographId))
                .ReturnsAsync((Monograph?)null);

            var result = await _controller.GetMonographAsync(monographId);

            Assert.IsInstanceOfType(result, typeof(NotFoundObjectResult));
        }

        [TestCategory("GetMonographAsync")]
        [TestMethod]
        public async Task GetMonographAsync_MonographDeleted_ReturnsNotFound()
        {
            ControllerTestHelper.SetupUnauthenticatedControllerContext(_controller);
            var monograph = MonographTestData.CreateMonograph(deleted: true, isExisting: true);
            var monographId = monograph.ItemId;
            _mockMonographRepository
                .Setup(x => x.FindByItemIdAsync(monographId))
                .ReturnsAsync(monograph);

            var result = await _controller.GetMonographAsync(monographId);

            Assert.IsInstanceOfType(result, typeof(NotFoundObjectResult));
        }

        [TestCategory("GetMonographAsync")]
        [TestMethod]
        public async Task GetMonographAsync_WithEncryptedContent_ReturnsMonograph()
        {
            ControllerTestHelper.SetupUnauthenticatedControllerContext(_controller);
            var monograph = MonographTestData.CreateEncryptedMonograph();
            var monographId = monograph.ItemId;
            _mockMonographRepository
                .Setup(x => x.FindByItemIdAsync(monographId))
                .ReturnsAsync(monograph);

            var result = await _controller.GetMonographAsync(monographId);

            Assert.IsInstanceOfType(result, typeof(OkObjectResult));
            var okResult = (OkObjectResult)result;
            Assert.IsNotNull(okResult.Value);
            var returnedMonograph = (Monograph)okResult.Value;
            Assert.AreEqual(monograph.ItemId, returnedMonograph.ItemId);
            Assert.IsNotNull(returnedMonograph.EncryptedContent);
            Assert.IsNull(returnedMonograph.Content);
        }

        [TestCategory("GetMonographAsync")]
        [TestMethod]
        public async Task GetMonographAsync_ItemIdIsNull_SetsItemIdToId()
        {
            ControllerTestHelper.SetupUnauthenticatedControllerContext(_controller);
            var monograph = MonographTestData.CreateMonograph(isExisting: true);
            monograph.ItemId = null;
            var monographId = monograph.Id;
            _mockMonographRepository
                .Setup(x => x.FindByItemIdAsync(monographId))
                .ReturnsAsync(monograph);

            var result = await _controller.GetMonographAsync(monographId);

            Assert.IsInstanceOfType(result, typeof(OkObjectResult));
            var okResult = (OkObjectResult)result;
            Assert.IsNotNull(okResult.Value);
            var returnedMonograph = (Monograph)okResult.Value;
            Assert.AreEqual(monograph.Id, returnedMonograph.ItemId);
        }

        [TestCategory("TrackView")]
        [TestMethod]
        public async Task TrackView_MonographNotFound_ReturnsSvgPixel()
        {
            ControllerTestHelper.SetupUnauthenticatedControllerContext(_controller);
            var monographId = "non-existent-id";
            _mockMonographRepository
                .Setup(x => x.FindByItemIdAsync(monographId))
                .ReturnsAsync((Monograph?)null);

            var result = await _controller.TrackView(monographId);

            Assert.IsInstanceOfType(result, typeof(ContentResult));
            _mockMonographRepository.Verify(
                x => x.SelfDestructAsync(It.IsAny<Monograph>(), It.IsAny<string>()),
                Times.Never);
            _mockSyncDeviceServiceWrapper.Verify(
                x => x.MarkMonographForSyncAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
                Times.Never);
            _mockMessengerService.Verify(
                x => x.SendTriggerSyncEventAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()),
                Times.Never);
        }

        [TestCategory("TrackView")]
        [TestMethod]
        public async Task TrackView_MonographDeleted_ReturnsSvgPixel()
        {
            ControllerTestHelper.SetupUnauthenticatedControllerContext(_controller);
            var monograph = MonographTestData.CreateMonograph(deleted: true, isExisting: true);
            var monographId = monograph.ItemId;
            _mockMonographRepository
                .Setup(x => x.FindByItemIdAsync(monographId))
                .ReturnsAsync(monograph);

            var result = await _controller.TrackView(monographId);

            Assert.IsInstanceOfType(result, typeof(ContentResult));
            _mockMonographRepository.Verify(
                x => x.SelfDestructAsync(It.IsAny<Monograph>(), It.IsAny<string>()),
                Times.Never);
            _mockSyncDeviceServiceWrapper.Verify(
                x => x.MarkMonographForSyncAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
                Times.Never);
            _mockMessengerService.Verify(
                x => x.SendTriggerSyncEventAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()),
                Times.Never);
        }

        [TestCategory("TrackView")]
        [TestMethod]
        public async Task TrackView_NonSelfDestructMonograph_ReturnsSvgPixel()
        {
            ControllerTestHelper.SetupUnauthenticatedControllerContext(_controller);
            var monograph = MonographTestData.CreateMonograph(isExisting: true, selfDestruct: false);
            var monographId = monograph.ItemId;
            _mockMonographRepository
                .Setup(x => x.FindByItemIdAsync(monographId))
                .ReturnsAsync(monograph);

            var result = await _controller.TrackView(monographId);

            Assert.IsInstanceOfType(result, typeof(ContentResult));
            _mockMonographRepository.Verify(
                x => x.SelfDestructAsync(It.IsAny<Monograph>(), It.IsAny<string>()),
                Times.Never);
            _mockSyncDeviceServiceWrapper.Verify(
                x => x.MarkMonographForSyncAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
                Times.Never);
            _mockMessengerService.Verify(
                x => x.SendTriggerSyncEventAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()),
                Times.Never);
        }

        [TestCategory("TrackView")]
        [TestMethod]
        public async Task TrackView_SelfDestructMonograph_DestroysMonographAndReturnsSvgPixel()
        {
            ControllerTestHelper.SetupUnauthenticatedControllerContext(_controller);
            ControllerTestHelper.SetupUnauthenticatedControllerContext(_controller);
            var monograph = MonographTestData.CreateMonograph(isExisting: true, selfDestruct: true);
            var monographId = monograph.ItemId;
            _mockMonographRepository
                .Setup(x => x.FindByItemIdAsync(monographId))
                .ReturnsAsync(monograph);
            var mockReplaceResult = new Mock<ReplaceOneResult>();
            mockReplaceResult.Setup(x => x.IsAcknowledged).Returns(true);
            _mockMonographRepository
                .Setup(x => x.SelfDestructAsync(monograph, monographId))
                .ReturnsAsync(mockReplaceResult.Object);

            var result = await _controller.TrackView(monographId);

            Assert.IsInstanceOfType(result, typeof(ContentResult));
            _mockSyncDeviceServiceWrapper.Verify(
                x => x.MarkMonographForSyncAsync(monograph.UserId, monographId, null),
                Times.Once);
            _mockMessengerService.Verify(
                x => x.SendTriggerSyncEventAsync("Monographs updated", monograph.UserId, null, true),
                Times.Once);
        }

        [TestCategory("DeleteAsync")]
        [TestMethod]
        public async Task DeleteAsync_ValidMonograph_DeletesSuccessfully()
        {
            var monograph = MonographTestData.CreateMonograph(isExisting: true);
            var deviceId = MonographTestData.TestDeviceId;
            var monographId = monograph.ItemId;
            _mockMonographRepository
                .Setup(x => x.FindByItemIdAsync(monographId))
                .ReturnsAsync(monograph);
            var mockReplaceResult = new Mock<ReplaceOneResult>();
            mockReplaceResult.Setup(x => x.IsAcknowledged).Returns(true);
            _mockMonographRepository
                .Setup(x => x.SoftDeleteAsync(MonographTestData.TestUserId, monograph, monographId))
                .ReturnsAsync(mockReplaceResult.Object);

            var result = await _controller.DeleteAsync(deviceId, monographId);

            Assert.IsInstanceOfType(result, typeof(OkResult));
            _mockMonographRepository.Verify(
                x => x.SoftDeleteAsync(MonographTestData.TestUserId, monograph, monographId),
                Times.Once);
            _mockSyncDeviceServiceWrapper.Verify(
                x => x.MarkMonographForSyncAsync(MonographTestData.TestUserId, monographId, deviceId),
                Times.Once);
            _mockMessengerService.Verify(
                x => x.SendTriggerSyncEventAsync("Monographs updated", MonographTestData.TestUserId, MonographTestData.TestJtiToken, false),
                Times.Once);
        }

        [TestCategory("DeleteAsync")]
        [TestMethod]
        public async Task DeleteAsync_MonographNotFound_ReturnsNotFound()
        {
            var deviceId = MonographTestData.TestDeviceId;
            var monographId = "non-existent-id";
            _mockMonographRepository
                .Setup(x => x.FindByItemIdAsync(monographId))
                .ReturnsAsync((Monograph?)null);

            var result = await _controller.DeleteAsync(deviceId, monographId);

            Assert.IsInstanceOfType(result, typeof(NotFoundObjectResult));
            _mockMonographRepository.Verify(
                x => x.SoftDeleteAsync(It.IsAny<string>(), It.IsAny<Monograph>(), It.IsAny<string>()),
                Times.Never);
            _mockSyncDeviceServiceWrapper.Verify(
                x => x.MarkMonographForSyncAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
                Times.Never);
            _mockMessengerService.Verify(
                x => x.SendTriggerSyncEventAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()),
                Times.Never);
        }

        [TestCategory("DeleteAsync")]
        [TestMethod]
        public async Task DeleteAsync_MonographAlreadyDeleted_ReturnsNotFound()
        {
            var monograph = MonographTestData.CreateMonograph(deleted: true, isExisting: true);
            var deviceId = MonographTestData.TestDeviceId;
            var monographId = monograph.ItemId;
            _mockMonographRepository
                .Setup(x => x.FindByItemIdAsync(monographId))
                .ReturnsAsync(monograph);

            var result = await _controller.DeleteAsync(deviceId, monographId);

            Assert.IsInstanceOfType(result, typeof(NotFoundObjectResult));
            _mockMonographRepository.Verify(
                x => x.SoftDeleteAsync(It.IsAny<string>(), It.IsAny<Monograph>(), It.IsAny<string>()),
                Times.Never);
            _mockSyncDeviceServiceWrapper.Verify(
                x => x.MarkMonographForSyncAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
                Times.Never);
            _mockMessengerService.Verify(
                x => x.SendTriggerSyncEventAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()),
                Times.Never);
        }

        [TestCategory("DeleteAsync")]
        [TestMethod]
        public async Task DeleteAsync_WithNullDeviceId_SkipsSyncOperations()
        {
            var monograph = MonographTestData.CreateMonograph(isExisting: true);
            string? deviceId = null;
            var monographId = monograph.ItemId;
            _mockMonographRepository
                .Setup(x => x.FindByItemIdAsync(monographId))
                .ReturnsAsync(monograph);
            var mockReplaceResult = new Mock<ReplaceOneResult>();
            mockReplaceResult.Setup(x => x.IsAcknowledged).Returns(true);
            _mockMonographRepository
                .Setup(x => x.SoftDeleteAsync(MonographTestData.TestUserId, monograph, monographId))
                .ReturnsAsync(mockReplaceResult.Object);

            var result = await _controller.DeleteAsync(deviceId, monographId);

            Assert.IsInstanceOfType(result, typeof(OkResult));
            _mockMonographRepository.Verify(
                x => x.SoftDeleteAsync(MonographTestData.TestUserId, monograph, monographId),
                Times.Once);
            _mockSyncDeviceServiceWrapper.Verify(
                x => x.MarkMonographForSyncAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
                Times.Never);
            _mockMessengerService.Verify(
                x => x.SendTriggerSyncEventAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()),
                Times.Never);
        }
    }
}
