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
    final t = await token();
    final h = <String, String>{'Content-Type': 'application/json'};
    if (t != null) h['Authorization'] = 'Bearer $t';
    final r = http.Request(method, Uri.parse('$baseUrl$path'))..headers.addAll(h);
    if (body != null) r.body = jsonEncode(body);
    final s = await r.send();
    final text = await s.stream.bytesToString();
    dynamic d;
    if (text.isNotEmpty) { try { d = jsonDecode(text); } catch (_) { d = text; } }
    if (s.statusCode < 200 || s.statusCode >= 300) throw Exception(d is String ? d : 'Request failed: ${s.statusCode}');
    return d;
  }

  Future<List<dynamic>> getList(String path) async => List<dynamic>.from(await _request('GET', path));
  Future<dynamic> get(String path) => _request('GET', path);
  Future<dynamic> patch(String path, Object body) => _request('PATCH', path, body: body);
  Future<dynamic> post(String path, Object body) => _request('POST', path, body: body);
  Future<dynamic> put(String path, Object body) => _request('PUT', path, body: body);

  Future<Map<String,dynamic>> login(String username, String password) async { final d=await _request('POST','/api/auth/login',body:{'username':username,'password':password}); await saveToken(d['token']); return Map<String,dynamic>.from(d); }
  Future<Map<String,dynamic>> me() async=>Map<String,dynamic>.from(await _request('GET','/api/me'));
  Future<Map<String,dynamic>> settings() async=>Map<String,dynamic>.from(await _request('GET','/api/settings'));

  Future<Map<String,dynamic>> createCustomerSession({String? name,String? phone,String? address}) async { final d=await _request('POST','/api/customer/session',body:{'name':name,'phone':phone,'address':address}); await saveToken(d['token']); return Map<String,dynamic>.from(d); }
  Future<List<dynamic>> products() async=>getList('/api/customer/products');
  Future<List<dynamic>> promotions() async=>getList('/api/customer/promotions');
  Future<List<dynamic>> orders() async=>getList('/api/customer/orders');
  Future<Map<String,dynamic>> createOrder({required List<Map<String,dynamic>> items,String orderType='Pickup',String? address,String? notes}) async=>Map<String,dynamic>.from(await _request('POST','/api/customer/orders',body:{'items':items,'orderType':orderType,'address':address,'notes':notes}));
  Future<Map<String,dynamic>> order(int id) async=>Map<String,dynamic>.from(await _request('GET','/api/customer/orders/$id'));

  Future<List<dynamic>> staffOrders({String? status}) async=>getList('/api/orders${status == null ? '' : '?status=${Uri.encodeQueryComponent(status)}'}');
  Future<Map<String,dynamic>> createStaffOrder({int? customerId,int? tableId,required List<Map<String,dynamic>> items,String orderType='Counter',String notes='',String? clientOperationId}) async=>Map<String,dynamic>.from(await post('/api/orders',{'customerId':customerId,'tableId':tableId,'items':items,'orderType':orderType,'notes':notes,'clientOperationId':clientOperationId}));
  Future<Map<String,dynamic>> updateOrderStatus(int id,String status) async=>Map<String,dynamic>.from(await patch('/api/orders/$id/status',{'status':status}));
  Future<Map<String,dynamic>> collectPayment(int id,{required double amountTendered,String method='Cash',String reference=''}) async=>Map<String,dynamic>.from(await post('/api/orders/$id/payment',{'amountTendered':amountTendered,'method':method,'reference':reference}));
  Future<List<dynamic>> orderPayments(int id) async=>getList('/api/orders/$id/payments');
  Future<List<dynamic>> tables({bool active=true}) async=>getList('/api/tables?active=$active');
  Future<List<dynamic>> discoverPrinters() async=>getList('/api/printers/discover');
  Future<Map<String,dynamic>> selectPrinter(String documentType) async=>Map<String,dynamic>.from(await post('/api/printers/select',{'documentType':documentType}));
  Future<Map<String,dynamic>> printReceipt(int orderId) async=>Map<String,dynamic>.from(await post('/api/printers/print-receipt/$orderId',{}));
  Future<dynamic> syncPull({String? since}) => _request('GET','/api/sync/pull${since == null ? '' : '?since=${Uri.encodeQueryComponent(since)}'}');
  Future<Map<String,dynamic>> syncPushOrders(List<Map<String,dynamic>> orders) async=>Map<String,dynamic>.from(await post('/api/sync/push-orders', orders));
  Future<List<dynamic>> syncConflicts() async=>getList('/api/sync/conflicts');

  Future<List<dynamic>> categories() async=>getList('/api/categories');
  Future<Map<String,dynamic>> createCategory(String name,{int sortOrder=0}) async=>Map<String,dynamic>.from(await post('/api/categories',{'name':name,'sortOrder':sortOrder}));
  Future<Map<String,dynamic>> createProduct({required int categoryId,required String name,required double price,String description='',String imageUrl='',bool available=true}) async=>Map<String,dynamic>.from(await post('/api/products',{'categoryId':categoryId,'name':name,'price':price,'description':description,'imageUrl':imageUrl,'available':available}));
  Future<Map<String,dynamic>> updateProduct(int id,{required int categoryId,required String name,required double price,String description='',String imageUrl='',bool available=true}) async=>Map<String,dynamic>.from(await put('/api/products/$id',{'categoryId':categoryId,'name':name,'price':price,'description':description,'imageUrl':imageUrl,'available':available}));

  Future<List<dynamic>> conversations() async=>getList('/api/messages/conversations');
  Future<List<dynamic>> messages(int id) async=>getList('/api/messages/$id');
  Future<Map<String,dynamic>> createConversation(List<String> participants,{String? title}) async=>Map<String,dynamic>.from(await post('/api/messages/conversations',{'participants':participants,'title':title}));
  Future<Map<String,dynamic>> sendMessage(int id,String text) async=>Map<String,dynamic>.from(await post('/api/messages/$id',{'text':text}));
  Future<void> markRead(int id) async{await _request('POST','/api/messages/$id/read');}
}
