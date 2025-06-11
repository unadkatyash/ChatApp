/**
 * Centralized Chat Manager for ASP.NET Core Chat Application
 * Handles SignalR connections, messaging, notifications, and UI updates
 */

class ChatManager {
    constructor() {
        this.connection = null;
        this.currentUser = null;
        this.currentUserId = null;
        this.currentUserFullName = null;
        this.receiverId = null;
        this.receiverName = null;
        this.currentRoom = 'general';

        // Page context
        this.pageType = this.detectPageType();

        // Initialize
        this.init();
    }

    detectPageType() {
        const path = window.location.pathname.toLowerCase();
        if (path.includes('/chat/privatechat')) return 'private';
        if (path.includes('/chat/index')) return 'group';
        if (path.includes('/chat/chatlist')) return 'list';
        return 'other';
    }

    async init() {
        // Set user context from page
        this.setUserContext();

        // Initialize connection
        await this.initializeConnection();

        // Setup event listeners
        this.setupEventListeners();

        // Page-specific initialization
        this.initializePageSpecific();

        // Request notification permission
        this.requestNotificationPermission();
    }

    setUserContext() {
        // Try to get user info from various sources
        const userNameElement = document.querySelector('[data-current-user]');
        const userIdElement = document.querySelector('[data-current-user-id]');
        const userFullNameElement = document.querySelector('[data-current-user-fullname]');
        const receiverIdElement = document.querySelector('[data-receiver-id]');
        const receiverNameElement = document.querySelector('[data-receiver-name]');
        const roomElement = document.querySelector('[data-room]');

        if (userNameElement) this.currentUser = userNameElement.getAttribute('data-current-user');
        if (userIdElement) this.currentUserId = userIdElement.getAttribute('data-current-user-id');
        if (userFullNameElement) this.currentUserFullName = userFullNameElement.getAttribute('data-current-user-fullname');
        if (receiverIdElement) this.receiverId = receiverIdElement.getAttribute('data-receiver-id');
        if (receiverNameElement) this.receiverName = receiverNameElement.getAttribute('data-receiver-name');
        if (roomElement) this.currentRoom = roomElement.getAttribute('data-room');
    }

    async getToken() {
        try {
            const response = await fetch('/Chat/GetToken');
            const data = await response.json();
            return data.token;
        } catch (error) {
            console.error('Error getting token:', error);
            return null;
        }
    }

    async initializeConnection() {
        const token = await this.getToken();

        if (!token) {
            console.error('No token available');
            this.updateConnectionStatus('Authentication Error', 'danger');
            return;
        }

        this.connection = new signalR.HubConnectionBuilder()
            .withUrl("/chatHub", {
                accessTokenFactory: () => token
            })
            .build();

        // Setup connection event handlers
        this.setupConnectionHandlers();

        try {
            await this.connection.start();
            this.updateConnectionStatus('Connected', 'success');

            // Page-specific connection setup
            await this.setupPageSpecificConnection();

        } catch (err) {
            console.error('SignalR Connection Error: ', err);
            this.updateConnectionStatus('Connection Failed', 'danger');
        }
    }

    setupConnectionHandlers() {
        // Private message handler
        this.connection.on("ReceivePrivateMessage", (senderId, senderName, message, time, messageId) => {
            this.handlePrivateMessage(senderId, senderName, message, time, messageId);
        });

        // Group message handler
        this.connection.on("ReceiveMessage", (sender, message, time, messageId, senderId) => {
            this.handleGroupMessage(sender, message, time, messageId, senderId);
        });

        // Private message sent confirmation
        this.connection.on("PrivateMessageSent", (toReceiverId, message, time, messageId) => {
            this.handlePrivateMessageSent(toReceiverId, message, time, messageId);
        });

        // Message read confirmation
        this.connection.on("MessageRead", (messageId, messageType) => {
            this.handleMessageRead(messageId, messageType);
        });

        // Unread count updates
        this.connection.on("UnreadMessageCount", (groupUnreadCount, privateUnreadCount) => {
            this.updateUnreadBadges(groupUnreadCount, privateUnreadCount);
        });

        // User joined/left
        this.connection.on("UserJoined", (userName, user) => {
            if (user !== this.currentUser) {
                this.addSystemMessage(userName + " joined the chat");
            }
        });

        this.connection.on("UserLeft", (userName, user) => {
            if (user !== this.currentUser) {
                this.addSystemMessage(userName + " left the chat");
            }
        });

        // Connection state handlers
        this.connection.onclose(() => {
            this.updateConnectionStatus('Disconnected', 'danger');
        });
    }

    async setupPageSpecificConnection() {
        switch (this.pageType) {
            case 'group':
                if (this.currentRoom) {
                    await this.connection.invoke("JoinRoom", this.currentRoom);
                }
                break;
            case 'private':
                await this.markMessagesAsRead();
                this.updateUserStatus();
                break;
            case 'list':
                await this.connection.invoke("GetUnreadMessageCount");
                break;
        }
    }

    handlePrivateMessage(senderId, senderName, message, time, messageId) {
        switch (this.pageType) {
            case 'private':
                if (senderId === this.receiverId) {
                    this.addMessageToChat(senderName, message, time, false, messageId);
                    this.markMessageAsRead(messageId, 'private');
                } else {
                    this.showNotification(senderName, message);
                }
                break;
            case 'list':
                this.updateChatList(senderId, senderName, message, time);
                this.updateUnreadCount();
                this.showNotification(senderName, message);
                break;
            default:
                this.showNotification(senderName, message);
        }
    }

    handleGroupMessage(sender, message, time, messageId, senderId) {
        switch (this.pageType) {
            case 'group':
                this.addMessageToChat(sender, message, time, sender === this.currentUserFullName, messageId);
                break;
            default:
                if (senderId !== this.currentUserId) {
                    this.updateUnreadCount();
                    this.showNotification(sender, message, true);
                }
        }
    }

    handlePrivateMessageSent(toReceiverId, message, time, messageId) {
        if (this.pageType === 'private' && toReceiverId === this.receiverId) {
            this.addMessageToChat("You", message, time, true, messageId);
        }
    }

    handleMessageRead(messageId, messageType) {
        if (this.pageType === 'private' && messageType === 'private') {
            this.updateMessageStatus(messageId, true);
        }
    }

    // UI Update Methods
    updateConnectionStatus(status, type) {
        const statusElements = document.querySelectorAll('[id*="connectionStatus"], [id*="ConnectionStatus"]');
        statusElements.forEach(element => {
            element.textContent = status;
            element.className = `badge bg-${type}`;
        });
    }

    addMessageToChat(sender, message, time, isOwnMessage, messageId) {
        const messagesList = document.getElementById("messagesList");
        if (!messagesList) return;

        const messageDiv = document.createElement("div");
        messageDiv.className = `message ${isOwnMessage ? 'own' : 'other'}`;
        if (messageId) messageDiv.setAttribute('data-message-id', messageId);

        const statusHtml = isOwnMessage && this.pageType === 'private' ?
            '<div class="message-status"><small class="chat-status">✓ Sent</small></div>' : '';

        const headerHtml = this.pageType === 'group' ?
            `<div class="message-header"><strong>${sender}</strong> - ${time}</div>` : '';

        messageDiv.innerHTML = `
            ${headerHtml}
            <div class="message-content">${this.escapeHtml(message)}</div>
            ${statusHtml}
        `;

        messagesList.appendChild(messageDiv);
        messagesList.scrollTop = messagesList.scrollHeight;
    }

    addSystemMessage(message) {
        const messagesList = document.getElementById("messagesList");
        if (!messagesList) return;

        const messageDiv = document.createElement("div");
        messageDiv.className = "text-center text-muted small mb-2";
        messageDiv.textContent = message;

        messagesList.appendChild(messageDiv);
        messagesList.scrollTop = messagesList.scrollHeight;
    }

    updateMessageStatus(messageId, isRead) {
        const messageElement = document.querySelector(`[data-message-id="${messageId}"]`);
        if (messageElement && messageElement.classList.contains('own')) {
            const statusElement = messageElement.querySelector('.message-status small');
            if (statusElement && isRead) {
                statusElement.innerHTML = '✓✓✓ Seen';
                statusElement.className = 'chat-status';
            }
        }
    }

    updateChatList(senderId, senderName, message, time) {
        const userChatItem = document.querySelector(`[data-user-id="${senderId}"]`);
        if (!userChatItem) return;

        const lastMessageElement = userChatItem.querySelector('.last-message');
        if (lastMessageElement) {
            lastMessageElement.textContent = message.length > 50 ? message.substring(0, 50) + '...' : message;
        }

        let unreadElement = userChatItem.querySelector('.unread-count');
        if (!unreadElement) {
            const unreadBadge = document.createElement('small');
            unreadBadge.className = 'badge bg-danger rounded-pill unread-count';
            unreadBadge.textContent = '1';
            userChatItem.querySelector('.d-flex.w-100.justify-content-between').appendChild(unreadBadge);
        } else {
            const currentCount = parseInt(unreadElement.textContent) || 0;
            unreadElement.textContent = currentCount + 1;
        }

        // Move to top
        const parent = userChatItem.parentElement;
        parent.insertBefore(userChatItem, parent.children[1]);
    }

    updateUnreadBadges(groupUnreadCount, privateUnreadCount) {
        const totalUnread = groupUnreadCount + privateUnreadCount;
        const unreadBadge = document.getElementById('unreadBadge');
        const groupUnreadElement = document.getElementById('groupUnreadCount');

        if (unreadBadge) {
            if (totalUnread > 0) {
                unreadBadge.textContent = totalUnread;
                unreadBadge.style.display = 'inline';
            } else {
                unreadBadge.style.display = 'none';
            }
        }

        if (groupUnreadElement) {
            if (groupUnreadCount > 0) {
                groupUnreadElement.textContent = groupUnreadCount;
                groupUnreadElement.style.display = 'inline';
            } else {
                groupUnreadElement.style.display = 'none';
            }
        }
    }

    // Messaging Methods
    async sendMessage(message, isPrivate = false) {
        if (!message || !this.connection) return;

        try {
            if (isPrivate && this.receiverId) {
                await this.connection.invoke("SendPrivateMessage", this.receiverId, message);
            } else {
                await this.connection.invoke("SendMessage", message, this.currentRoom);
            }
        } catch (err) {
            console.error('Send Message Error: ', err);
            alert('Failed to send message. Please try again.');
        }
    }

    async markMessageAsRead(messageId, messageType) {
        if (this.connection) {
            try {
                await this.connection.invoke("MarkMessageAsRead", messageId, messageType);
            } catch (err) {
                console.error('Error marking message as read:', err);
            }
        }
    }

    async markMessagesAsRead() {
        if (this.pageType !== 'private' || !this.receiverId) return;

        try {
            const response = await fetch('/Chat/MarkMessagesAsRead', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(this.receiverId)
            });
            const result = await response.json();
            console.log('Marked messages as read:', result.count);
        } catch (err) {
            console.error('Error marking messages as read:', err);
        }
    }

    async updateUnreadCount() {
        if (this.connection) {
            try {
                await this.connection.invoke("GetUnreadMessageCount");
            } catch (err) {
                console.error('Error getting unread count:', err);
            }
        }
    }

    // Notification Methods
    showNotification(senderName, message, isGroup = false) {
        if ('Notification' in window && Notification.permission === 'granted') {
            const title = isGroup ? `New message in General Chat` : `New message from ${senderName}`;
            const options = {
                body: message.length > 100 ? message.substring(0, 100) + '...' : message,
                icon: '/favicon.ico',
                badge: '/favicon.ico',
                tag: isGroup ? 'group' : senderName
            };

            const notification = new Notification(title, options);

            notification.onclick = () => {
                window.focus();
                if (isGroup) {
                    window.location.href = '/Chat/Index';
                } else {
                    const userChatItem = Array.from(document.querySelectorAll('.user-chat-item')).find(item =>
                        item.textContent.includes(senderName)
                    );
                    if (userChatItem) {
                        const userId = userChatItem.getAttribute('data-user-id');
                        window.location.href = `/Chat/PrivateChat?userId=${userId}`;
                    }
                }
                notification.close();
            };

            setTimeout(() => notification.close(), 5000);
        }
    }

    requestNotificationPermission() {
        if ('Notification' in window && Notification.permission === 'default') {
            Notification.requestPermission();
        }
    }

    // Page-specific initialization
    initializePageSpecific() {
        switch (this.pageType) {
            case 'group':
                this.initializeGroupChat();
                break;
            case 'private':
                this.initializePrivateChat();
                break;
            case 'list':
                this.initializeChatList();
                break;
        }
    }

    initializeGroupChat() {
        this.loadOnlineUsers();
        setInterval(() => this.loadOnlineUsers(), 5000);
    }

    initializePrivateChat() {
        // Auto-scroll to bottom
        const messagesList = document.getElementById("messagesList");
        if (messagesList) {
            messagesList.scrollTop = messagesList.scrollHeight;
        }

        // Update user status every 10 seconds
        setInterval(() => this.updateUserStatus(), 10000);

        // Setup user item click handlers
        document.querySelectorAll('.user-item').forEach(item => {
            item.addEventListener('click', function () {
                const url = this.getAttribute('data-url');
                if (url) window.location.href = url;
            });
        });
    }

    initializeChatList() {
        setInterval(() => this.updateUnreadCount(), 30000);
    }

    // Event Listeners
    setupEventListeners() {
        // Send button
        const sendButton = document.getElementById("sendButton");
        if (sendButton) {
            sendButton.addEventListener("click", () => this.handleSendMessage());
        }

        // Message input
        const messageInput = document.getElementById("messageInput");
        if (messageInput) {
            messageInput.addEventListener("keypress", (e) => {
                if (e.key === "Enter") {
                    this.handleSendMessage();
                }
            });
        }

        // Page unload
        window.addEventListener("beforeunload", () => {
            if (this.connection) {
                this.connection.stop();
            }
        });
    }

    handleSendMessage() {
        const messageInput = document.getElementById("messageInput");
        if (!messageInput) return;

        const message = messageInput.value.trim();
        if (!message) return;

        const isPrivate = this.pageType === 'private';
        this.sendMessage(message, isPrivate);
        messageInput.value = "";
    }

    // Utility Methods
    escapeHtml(text) {
        const div = document.createElement('div');
        div.textContent = text;
        return div.innerHTML;
    }

    updateUserStatus() {
        if (this.pageType !== 'private') return;

        const userElement = document.querySelector(`[data-user-id="${this.receiverId}"]`);
        const statusElement = document.getElementById('userStatus');

        if (userElement && statusElement) {
            const isOnline = !userElement.classList.contains('offline');
            if (isOnline) {
                statusElement.textContent = 'Online';
                statusElement.className = 'text-success';
            } else {
                statusElement.textContent = 'Offline';
                statusElement.className = 'text-muted';
            }
        }
    }

    async loadOnlineUsers() {
        if (this.pageType !== 'group') return;

        try {
            const response = await fetch('/Chat/GetOnlineUsers');
            const users = await response.json();

            const usersList = document.getElementById("usersList");
            if (!usersList) return;

            usersList.innerHTML = "";

            if (users.length === 0) {
                usersList.innerHTML = "<div class='text-muted'>No users available.</div>";
                return;
            }

            users.sort((a, b) => {
                if (a.userName === this.currentUser) return -1;
                if (b.userName === this.currentUser) return 1;
                if (a.isOnline && !b.isOnline) return -1;
                if (!a.isOnline && b.isOnline) return 1;
                return 0;
            });

            users.forEach(user => {
                const div = document.createElement("div");
                const isCurrentUser = user.userName === this.currentUser;
                const isOnline = user.isOnline;

                div.className = isOnline ? "user-item" : "user-item offline";

                const dotClass = isOnline ? "text-success" : "text-muted";
                const dot = `<span class="${dotClass} me-1">●</span>`;
                const name = isCurrentUser
                    ? `<strong>You (${user.firstName} ${user.lastName})</strong>`
                    : `${user.firstName} ${user.lastName}`;

                const status = isOnline
                    ? "<span class='text-success ms-2'>Online</span>"
                    : `<div class='text-muted ms-4'>Last seen: ${this.formatDateTime(user.lastSeen)}</div>`;

                div.innerHTML = `${dot} ${name}<br>${status}`;
                usersList.appendChild(div);
            });
        } catch (err) {
            console.error("Failed to load users", err);
        }
    }

    formatDateTime(dateTimeStr) {
        const date = new Date(dateTimeStr);
        const now = new Date();

        const oneDay = 24 * 60 * 60 * 1000;
        const diffTime = now - date;
        const diffDays = Math.floor(diffTime / oneDay);

        const isToday = date.toDateString() === now.toDateString();

        const yesterday = new Date();
        yesterday.setDate(now.getDate() - 1);
        const isYesterday = date.toDateString() === yesterday.toDateString();

        if (isToday) {
            return date.toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' });
        } else if (isYesterday) {
            return "Yesterday";
        } else if (diffDays < 7) {
            return date.toLocaleDateString(undefined, { weekday: 'long' });
        } else {
            return `${date.toLocaleDateString(undefined, { month: 'short', day: 'numeric' })}, ${date.toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' })}`;
        }
    }

    // Public API for external access
    async invoke(method, ...args) {
        if (this.connection) {
            return await this.connection.invoke(method, ...args);
        }
    }

    on(event, callback) {
        if (this.connection) {
            this.connection.on(event, callback);
        }
    }

    stop() {
        if (this.connection) {
            this.connection.stop();
        }
    }
}

// Global instance
let chatManager;

// Initialize when DOM is ready
document.addEventListener('DOMContentLoaded', function () {
    chatManager = new ChatManager();
});

// Export for module systems
if (typeof module !== 'undefined' && module.exports) {
    module.exports = ChatManager;
}