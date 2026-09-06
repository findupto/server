import 'dart:async';
import 'package:signalr_netcore/signalr_client.dart';
import 'api_client.dart';

class PosRealtime {
  PosRealtime(this.api); final PosApiClient api; HubConnection? _hub; StreamSubscription? _connectionSubscription; final events=StreamController<Map<String,dynamic>>.broadcast(); bool _disposed=false; bool _connecting=false;
  Future<void> connect()async{if(_disposed||_connecting)return;_connecting=true;try{final token=await api.token();if(token==null||token.isEmpty||_disposed)return;await _hub?.stop();final hub=HubConnectionBuilder().withUrl('${api.baseUrl}/hubs/pos',options:HttpConnectionOptions(accessTokenFactory:()async=>(await api.token())??'')).withAutomaticReconnect().build();_hub=hub;for(final name in ['order.updated','message.created','messages.read','typing.changed','call.signal','payment.updated','rider.updated','rider.location','delivery.updated','location.updated','notification.created']){hub.on(name,(args){if(!_disposed)events.add({'type':name,'data':args?.isNotEmpty==true?args!.first:null});});}await hub.start();await hub.invoke('JoinUserGroup');}finally{_connecting=false;}}
  Future<void> joinTracking(String code)async{await _hub?.invoke('JoinTrackingGroup',args:[code]);}
  Future<void> joinConversation(int id)async{await _hub?.invoke('JoinConversationGroup',args:[id]);} Future<void> typing(int id,bool value)async{await _hub?.invoke('Typing',args:[id,value]);} Future<void> reconnect()=>connect(); Future<void> dispose()async{if(_disposed)return;_disposed=true;await _connectionSubscription?.cancel();await _hub?.stop();_hub=null;await events.close();}
}
