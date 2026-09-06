import 'dart:convert';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:http/http.dart' as http;

class PosApiClient {
  PosApiClient({required this.baseUrl, FlutterSecureStorage? storage}) : _storage = storage ?? const FlutterSecureStorage();
  final String baseUrl;
  final FlutterSecureStorage _storage;
  Future<void> saveToken(String token) => _storage.write(key: 'pos_jwt', value: token);
  Future<String?> token() => _storage.read(key: 'pos_jwt');
  Future<void> clearToken() => _storage.delete(key: 'pos_jwt');
  Future<dynamic> _request(String method, String path, {Object? body}) async {
    final tokenValue = await token();
    final headers = <String, String>{'Content-Type': 'application/json'};
    if (tokenValue != null) headers['Authorization'] = 'Bearer $tokenValue';
    final request = http.Request(method, Uri.parse('$baseUrl$path'))..headers.addAll(headers);
    if (body != null) request.body = jsonEncode(body);
    final response = await request.send(); final text = await response.stream.bytesToString();
    dynamic data; if (text.isNotEmpty) { try { data = jsonDecode(text); } catch (_) { data = text; } }
    if (response.statusCode < 200 || response.statusCode >= 300) throw Exception(data is String ? data : 'Request failed: ${response.statusCode}');
    return data;
  }
  Future<Map<String, dynamic>> createCustomerSession({String? name, String? phone, String? address}) async { final data = await _request('POST', '/api/customer/session', body: {'name': name, 'phone': phone, 'address': address}); await saveToken(data['token'] as String); return Map<String, dynamic>.from(data); }
  Future<List<dynamic>> products() async => List<dynamic>.from(await _request('GET', '/api/customer/products'));
  Future<List<dynamic>> promotions() async => List<dynamic>.from(await _request('GET', '/api/customer/promotions'));
  Future<List<dynamic>> orders() async => List<dynamic>.from(await _request('GET', '/api/customer/orders'));
  Future<Map<String, dynamic>> createOrder({required List<Map<String, dynamic>> items, String orderType = 'Pickup', String? address, String? notes}) async => Map<String, dynamic>.from(await _request('POST', '/api/customer/orders', body: {'items': items, 'orderType': orderType, 'address': address, 'notes': notes}));
  Future<Map<String, dynamic>> order(int id) async => Map<String, dynamic>.from(await _request('GET', '/api/customer/orders/$id'));
  Future<List<dynamic>> conversations() async => List<dynamic>.from(await _request('GET', '/api/messages/conversations'));
  Future<List<dynamic>> messages(int conversationId) async => List<dynamic>.from(await _request('GET', '/api/messages/$conversationId'));
  Future<Map<String, dynamic>> createConversation(List<String> participants, {String? title}) async => Map<String, dynamic>.from(await _request('POST', '/api/messages/conversations', body: {'participants': participants, 'title': title}));
  Future<Map<String, dynamic>> sendMessage(int conversationId, String text) async => Map<String, dynamic>.from(await _request('POST', '/api/messages/$conversationId', body: {'text': text}));
  Future<void> markRead(int conversationId) async { await _request('POST', '/api/messages/$conversationId/read'); }
}
