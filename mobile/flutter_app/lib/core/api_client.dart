import 'dart:convert';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:http/http.dart' as http;

class PosApiClient {
  PosApiClient({required this.baseUrl, FlutterSecureStorage? storage}) : _storage = storage ?? const FlutterSecureStorage();
  final String baseUrl; final FlutterSecureStorage _storage;
  Future<void> saveToken(String token) => _storage.write(key: 'pos_jwt', value: token);
  Future<String?> token() => _storage.read(key: 'pos_jwt');
  Future<void> clearToken() => _storage.delete(key: 'pos_jwt');
  Future<dynamic> _request(String method, String path, {Object? body}) async {
    final t = await token(); final h = <String, String>{'Content-Type':'application/json'}; if (t != null) h['Authorization']='Bearer $t';
    final r = http.Request(method, Uri.parse('$baseUrl$path'))..headers.addAll(h); if (body != null) r.body=jsonEncode(body);
    final s=await r.send(); final text=await s.stream.bytesToString(); dynamic d; if(text.isNotEmpty){try{d=jsonDecode(text);}catch(_){d=text;}}
    if(s.statusCode<200||s.statusCode>=300) throw Exception(d is String?d:'Request failed: ${s.statusCode}'); return d;
  }
  Future<List<dynamic>> getList(String path) async => List<dynamic>.from(await _request('GET', path));
  Future<dynamic> patch(String path, Object body) => _request('PATCH', path, body: body);
  Future<dynamic> post(String path, Object body) => _request('POST', path, body: body);
  Future<Map<String,dynamic>> createCustomerSession({String? name,String? phone,String? address}) async { final d=await _request('POST','/api/customer/session',body:{'name':name,'phone':phone,'address':address}); await saveToken(d['token']); return Map<String,dynamic>.from(d); }
  Future<List<dynamic>> products() async=>getList('/api/customer/products');
  Future<List<dynamic>> promotions() async=>getList('/api/customer/promotions');
  Future<List<dynamic>> orders() async=>getList('/api/customer/orders');
  Future<Map<String,dynamic>> createOrder({required List<Map<String,dynamic>> items,String orderType='Pickup',String? address,String? notes}) async=>Map<String,dynamic>.from(await _request('POST','/api/customer/orders',body:{'items':items,'orderType':orderType,'address':address,'notes':notes}));
  Future<Map<String,dynamic>> order(int id) async=>Map<String,dynamic>.from(await _request('GET','/api/customer/orders/$id'));
  Future<List<dynamic>> conversations() async=>getList('/api/messages/conversations');
  Future<List<dynamic>> messages(int id) async=>getList('/api/messages/$id');
  Future<Map<String,dynamic>> createConversation(List<String> participants,{String? title}) async=>Map<String,dynamic>.from(await _request('POST','/api/messages/conversations',body:{'participants':participants,'title':title}));
  Future<Map<String,dynamic>> sendMessage(int id,String text) async=>Map<String,dynamic>.from(await _request('POST','/api/messages/$id',body:{'text':text}));
  Future<void> markRead(int id) async{await _request('POST','/api/messages/$id/read');}
}
