@docker build . -f samples\SampleApp\Dockerfile -t localhost:5000/sampleapp:%1 
@docker push localhost:5000/sampleapp:%1  
@kubectl apply -f .\samples\SampleApp\sampleapp.yml   
@kubectl get pods